// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

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
