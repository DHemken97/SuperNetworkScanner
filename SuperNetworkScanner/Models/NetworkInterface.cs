using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperNetworkScanner.Models
{
    public class NetworkInterface
    {
        public string Name { get; set; }
        public string MAC { get; set; }
        public List<string> Ip_Address { get; set; }
    }
}
