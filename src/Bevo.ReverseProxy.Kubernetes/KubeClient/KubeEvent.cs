// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using k8s.Models;

namespace Bevo.ReverseProxy.Kube
{
    public class KubeEvent
    {
        public V1ObjectReference Regarding { get; set; }

        public DateTime Timestamp { get; set; }

        public EventType EvtType { get; set; }

        public string Reason { get; set; }

        public string Message { get; set; }

        public enum EventType
        {
            Normal,

            Warning,
        }
    }
}