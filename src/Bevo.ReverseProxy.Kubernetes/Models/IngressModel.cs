// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Collections.Generic;
using k8s.Models;

namespace Bevo.ReverseProxy.Kube
{
    // TODO Add annotations
    public struct IngressModel
    {
        public string Namespace { get; set; }

        public string Name { get; set; }

        public IEnumerable<IngressRule> Rules { get; set; }

        internal Extensionsv1beta1Ingress Original { get; set; }
    }
}
