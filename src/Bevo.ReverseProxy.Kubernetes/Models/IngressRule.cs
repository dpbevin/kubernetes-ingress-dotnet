// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Collections.Generic;

namespace Bevo.ReverseProxy.Kube
{
    public struct IngressRule
    {
        public string Host { get; set; }

        public IEnumerable<IngressPath> Paths { get; set; }
    }

}
