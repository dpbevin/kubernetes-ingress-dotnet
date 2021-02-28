
using System;
using System.Collections.Generic;

namespace Bevo.ReverseProxy.Kube
{
    // TODO Add annotations
    public struct IngressModel
    {
        public string Namespace { get; set; }

        public string Name { get; set; }

        public IEnumerable<IngressRule> Rules { get; set; }
    }
}
