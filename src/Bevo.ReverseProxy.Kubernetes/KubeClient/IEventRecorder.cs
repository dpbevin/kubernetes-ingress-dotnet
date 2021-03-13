// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Threading;
using System.Threading.Tasks;
using k8s.Models;

namespace Bevo.ReverseProxy.Kube
{
    public interface IEventRecorder
    {
        ValueTask CreateEvent(V1ObjectReference runtimeObject, KubeEvent.EventType eventType, string reason, string message, CancellationToken cancellation);
    }
}