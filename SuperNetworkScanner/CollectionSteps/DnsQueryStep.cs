using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SuperNetworkScanner.Models;

namespace SuperNetworkScanner.CollectionSteps
{
    public class DNSQueryStep : ICollectionStep
    {
        public string Name => "DNS Sweep";

        public string Description => "Try to get Hostnames by DNS Query";

        public string ProgressMessage { get; private set; }

        public string ProgressLog { get; private set; } = string.Empty;

        public decimal ProgressPercentage { get; private set; }

        public bool IsCompleted { get; private set; }
        public bool SkipOffline { get; set; }


        private readonly object _logLock = new();
        private readonly object _progressLock = new();

        public void Start(List<string> search_ips)
        {
            int total = search_ips.Count;
            int completed = 0;
            int maxParallelism = 20;
            var search = search_ips;
            if (SkipOffline)
                search = NetworkMap.Hosts.Where(x => x.Status == HostStatus.Online).SelectMany(x => x.NetworkInterfaces.SelectMany(xx => xx.Ip_Address)).Distinct().ToList();

            var tasks = Partitioner.Create(search)
                                   .GetPartitions(maxParallelism)
                                   .Select(partition => Task.Run(() =>
                                   {
                                       using (partition)
                                       {
                                           while (partition.MoveNext())
                                           {
                                               string ip = partition.Current;
                                               string result = $"Querying hostname for {ip}...";

                                               try
                                               {
                                                   var entry = Dns.GetHostEntry(ip);
                                                   var hostname = entry?.HostName;

                                                   if (!string.IsNullOrWhiteSpace(hostname))
                                                   {
                                                       var host = NetworkMap.Hosts
                                                           .FirstOrDefault(h => h.NetworkInterfaces
                                                           .Any(ni => ni.Ip_Address.Contains(ip)));

                                                       if (host != null)
                                                       {
                                                           host.Hostname = hostname;
                                                           result += $"Found: {hostname}";
                                                       }
                                                       else
                                                       {
                                                           result += "Not in host list, skipped.";
                                                       }
                                                   }
                                                   else
                                                   {
                                                       result += "No hostname found.";
                                                   }
                                               }
                                               catch
                                               {
                                                   result += "DNS lookup failed.";
                                               }

                                               lock (_logLock)
                                               {
                                                   ProgressLog += result + "\r\n";
                                               }

                                               lock (_progressLock)
                                               {
                                                   completed++;
                                                   ProgressPercentage = (decimal)completed / total;
                                                   ProgressMessage = $"Completed {completed} of {total}";
                                               }
                                           }
                                       }
                                   })).ToArray();

            Task.WaitAll(tasks);
            IsCompleted = true;
        }
    }
}
