﻿// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Bevo.ReverseProxy.Kube
{
    public class EventRecorder : IEventRecorder
    {
        private readonly Channel<KubeEvent> _channel;

        private readonly ILogger _logger;

        public EventRecorder(Channel<KubeEvent> channel, ILogger<EventRecorder> logger)
        {
            _channel = channel;
            _logger = logger;
        }

        public async ValueTask CreateEvent(V1ObjectReference runtimeObject, KubeEvent.EventType eventType, string reason, string message, CancellationToken cancellation)
        {
            var kubeEvent = new KubeEvent
            {
                Regarding = runtimeObject,
                Timestamp = DateTime.UtcNow,
                EvtType = eventType,
                Reason = reason,
                Message = message,
            };

            _logger.LogInformation($"Event(Kind={runtimeObject.Kind}, Resource={runtimeObject.NamespaceProperty}/{runtimeObject.Name}, UID={runtimeObject.Uid}, APIVersion={runtimeObject.ApiVersion}, ResourceVersion={runtimeObject.ResourceVersion}, type={eventType}, reason='{reason}', '{message}')");

            await _channel.Writer.WriteAsync(kubeEvent, cancellation);
        }
    }
}
