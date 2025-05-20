using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperNetworkScanner.CollectionSteps
{
    public class DNSQueryStep : ICollectionStep
    {
        public string Name => "DNS Sweep";

        public string Description => "Try to get Hostnames by DNS Query";

        public string ProgressMessage { get; private set; }

        public string ProgressLog { get; private set; }

        public decimal ProgressPercentage { get; private set; }

        public bool IsCompleted { get; private set; }

        public void Start()
        {//simulate for now
            for (int i = 1; i <= 254; i++)
            {

                ProgressPercentage = (decimal)i / 254m;
                ProgressMessage = $"WhoIs 192.168.1.{i}?";
                ProgressLog += ProgressMessage + "...";
                Thread.Sleep(100);
                ProgressLog += "Found\r\n";
                var name = $"Machine {i} Test";
                var host = NetworkMap.Hosts.FirstOrDefault(x => x.NetworkInterfaces.Any(y => y.Ip_Address.Contains($"192.168.1.{i}")));
                host.Hostname = name;
            }
            IsCompleted = true;
        }
    }
}
