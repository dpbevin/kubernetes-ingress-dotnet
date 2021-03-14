// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bevo.ReverseProxy.Kube
{
    public class EventBroadcaster : BackgroundService
    {
        private readonly Channel<KubeEvent> _channel;

        private readonly IKubernetes _client;

        private readonly IControllerConfiguration _configuration;

        private readonly ILogger _logger;

        public EventBroadcaster(Channel<KubeEvent> channel, IKubernetes client, IControllerConfiguration configuration, ILogger<EventBroadcaster> logger)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var item = await _channel.Reader.ReadAsync(stoppingToken);

                try
                {
                    await RecordToSink(item, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process event");
                }
            }
        }

        private async Task RecordToSink(KubeEvent item, CancellationToken cancellation)
        {
            // recorder.makeEvent(refRegarding, refRelated, timestamp, eventtype, reason, message, recorder.reportingController, recorder.reportingInstance, action)
            // See https://github.com/kubernetes/client-go/blob/758467711e075d6fd3d31abcaf6e2e1eb51ef3d4/tools/events/event_recorder.go#L66
            var evt = new Eventsv1Event
            {
                ApiVersion = Eventsv1Event.KubeGroup + "/" + Eventsv1Event.KubeApiVersion,
                Kind = Eventsv1Event.KubeKind,
                Regarding = item.Regarding,
                Reason = item.Reason,
                Type = item.EvtType.ToString(),
                Metadata = new V1ObjectMeta
                {
                    Name = $"{item.Regarding.Name}.{item.Timestamp.Ticks:D}",
                    NamespaceProperty = item.Regarding.NamespaceProperty,
                },
                Note = item.Message,
                EventTime = item.Timestamp,
                ReportingController = "dotnet-ingress-controller",
                ReportingInstance = _configuration.PodName,
                Action = "Added",
            };

            try
            {
                var createdEvent = await _client.CreateNamespacedEvent1Async(evt, namespaceParameter: item.Regarding.NamespaceProperty, cancellationToken: cancellation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to report event for {namespace}/{name}", item.Regarding.NamespaceProperty, item.Regarding.Name);
            }
        }
    }
}
