using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bevo.ReverseProxy.Kube
{
    public struct ServiceModel
    {
        public string Namespace { get; set; }

        public string Name { get; set; }
    }
}
