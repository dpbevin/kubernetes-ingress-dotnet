
using System;
using System.Collections.Generic;

namespace Bevo.ReverseProxy.Kube
{
    public struct ServicePortModel
    {
        public string Namespace { get; set; }

        public string ServiceName { get; set; }

        public string PortName { get; set; }

        public string Protocol { get; set; }

        public int Port { get; set; }
    }
}
