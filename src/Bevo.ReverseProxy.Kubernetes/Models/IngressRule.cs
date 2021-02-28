
using System;
using System.Collections.Generic;

namespace Bevo.ReverseProxy.Kube
{
    public struct IngressRule
    {
        public string Host { get; set; }

        public IEnumerable<IngressPath> Paths { get; set; }
    }

}
