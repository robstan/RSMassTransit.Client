﻿/*
    Copyright (C) 2018 Jeffrey Sharp

    Permission to use, copy, modify, and distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
    ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using RSMassTransit.Messages;
using static System.Reflection.BindingFlags;
using static System.StringComparison;

namespace RSMassTransit.Client
{
    /// <summary>
    ///   Base class for RSMassTransit clients.
    /// </summary>
    public abstract class ReportingServicesClient : IReportingServicesClient
    {
        private readonly IBusControl _bus;
        private readonly Uri         _queueUri;
        private int                  _isDisposed;

        /// <summary>
        ///   Creates a new <see cref="ReportingServicesClient"/> instance with
        ///   the specified configuration.
        /// </summary>
        /// <param name="configuration">
        ///   The configuration for the client, specifying how to communicate
        ///   with RSMassTransit.
        /// </param>
        protected ReportingServicesClient(ReportingBusConfiguration configuration)
        {
            Configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));

            _bus = CreateBus(out _queueUri);
        }

        /// <summary>
        ///   The configuration of the client, specifying how to communicate
        ///   with RSMassTransit.
        /// </summary>
        public ReportingBusConfiguration Configuration { get; }

        /// <summary>
        ///   When implemented in a derived class, creates the message bus
        ///   instance used to communicate with RSMassTransit.
        /// </summary>
        /// <param name="queueUri">
        ///   When this method returns, contains the normalized URI of the bus
        ///   queue used to send and receive messages.
        /// </param>
        /// <returns>
        ///   The message bus instance on which to send and receive messages.
        /// </returns>
        protected abstract IBusControl CreateBus(out Uri queueUri);

        /// <inheritdoc/>
        public Task<IExecuteReportResponse> ExecuteAsync(
            IExecuteReportRequest request,
            TimeSpan?             timeout           = default,
            CancellationToken     cancellationToken = default)
            => SendAsync<IExecuteReportRequest, IExecuteReportResponse>(request);

        private Task<TResponse> SendAsync<TRequest, TResponse>(
            TRequest          request,
            TimeSpan?         timeout           = default,
            CancellationToken cancellationToken = default)
            where TRequest  : class
            where TResponse : class
            => _bus
                .CreateRequestClient<TRequest, TResponse>(
                    _queueUri,
                    timeout ?? Configuration.RequestTimeout
                )
                .Request(request, cancellationToken);

        /// <summary>
        ///   Creates a new <see cref="ReportingServicesClient"/> instance with
        ///   the specified configuration.
        /// </summary>
        /// <param name="configuration">
        ///   The configuration for the client, specifying how to communicate
        ///   with RSMassTransit.
        /// </param>
        public static ReportingServicesClient Create(ReportingBusConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var supportedSchemes = DiscoverSupportedSchemes();
            var requestedScheme  = configuration.BusUri?.Scheme;

            if (!supportedSchemes.TryGetValue(requestedScheme, out Type type))
                throw OnUnsupportedScheme(requestedScheme, supportedSchemes.Keys);

            return (ReportingServicesClient) Activator.CreateInstance(type, configuration);
        }

        private static SortedDictionary<string, Type> DiscoverSupportedSchemes()
        {
            var schemes = new SortedDictionary<string, Type>(StringComparer.Ordinal);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Assembly must be from RSMassTransit.Client family
                if (!assembly.GetName().Name.StartsWith("RSMassTransit.Client.", Ordinal))
                    continue;

                foreach (var type in assembly.GetExportedTypes())
                {
                    // Type must be a concrete client class
                    if (!typeof(ReportingServicesClient).IsAssignableFrom(type) || type.IsAbstract)
                        continue;

                    // Type must declare a URI scheme
                    var schemeField = type.GetField("UriScheme", Public | Static);
                    if (schemeField.FieldType != typeof(string))
                        continue;

                    // URI scheme must be non-null
                    var scheme = schemeField.GetValue(obj: null) as string;
                    if (scheme != null)
                        schemes.Add(scheme, type);
                }
            }

            return schemes;
        }

        private static Exception OnUnsupportedScheme(
            string              requestedScheme,
            IEnumerable<string> supportedSchemes)
        {
            var message = new StringBuilder();

            message.AppendFormat(
                "The URI scheme '{0}' is not supported by any loaded RSMassTransit client type.  ",
                requestedScheme
            );

            if (supportedSchemes.Any())
                message.Append("Supported schemes are: ")
                    .AppendDelimitedList(supportedSchemes);
            else
                message.Append(
                    "No RSMassTransit client types are loaded.  " +
                    "Did you forget to install a client package?"
                );

            return new ArgumentException(message.ToString(), "configuration");
        }

        /// <summary>
        ///   Stops the bus instance used by the client and releases any
        ///   managed or unmanaged resources owned by the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(managed: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///   Invoked by either <see cref="Dispose()"/> or the finalizer thread.
        ///   Stops the bus instance used by the client and releases any
        ///   resources of the specified kind owned by the client.
        /// </summary>
        /// <param name="managed">
        ///   <c>true</c> to dispose managed an unmanaged resources;
        ///   <c>false</c> to dispose only unamanged resources.
        /// </param>
        /// <remarks>
        ///   The current <see cref="ReportingServicesClient"/> implementation
        ///   does not expect to own unmanaged resources and thus does not
        ///   provide a finalizer.  Thus the <paramref name="managed"/>
        ///   parameter always will be <c>true</c>.
        /// </remarks>
        protected virtual void Dispose(bool managed)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
                return; // already disposed

            if (managed)
                _bus?.Stop();
        }
    }
}
