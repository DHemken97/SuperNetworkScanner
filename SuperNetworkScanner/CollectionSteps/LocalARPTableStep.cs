using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using SuperNetworkScanner.Models;

namespace SuperNetworkScanner.CollectionSteps
{
    public class LocalARPTableStep : ICollectionStep
    {
        public string Name => "ARP Table Search";

        public string Description => "Try to get MAC addresses by ARP Table Query";

        public string ProgressMessage { get; private set; }

        public string ProgressLog { get; private set; } = string.Empty;

        public decimal ProgressPercentage { get; private set; }

        public bool IsCompleted { get; private set; }

        public void Start(List<string> search_ips)
        {
            var arpEntries = GetArpTable();
            int total = arpEntries.Count;
            int processed = 0;

            foreach (var entry in arpEntries)
            {
                processed++;
                if (!search_ips.Contains(entry.IpAddress)) continue;
                ProgressPercentage = (decimal)processed / total;
                ProgressMessage = $"Processing ARP entry {entry.IpAddress}";
                ProgressLog += ProgressMessage + "...";
                var host = NetworkMap.Hosts.FirstOrDefault(h =>
                    h.NetworkInterfaces.Any(ni => ni.Ip_Address.Contains(entry.IpAddress)));

                if (host == null)
                {

                    // Host not found, add it
                    NetworkMap.Hosts.Add(new Host
                    {
                        NetworkInterfaces = new List<NetworkInterface>
                        {
                            new NetworkInterface
                            {
                                Ip_Address = new List<string>{ entry.IpAddress } ,
                                MAC = entry.MacAddress
                            }
                        }
                    });
                    ProgressLog += "Added new host from ARP.\r\n";
                }
                else
                {
                    // Host exists, check for missing MAC
                    foreach (var iface in host.NetworkInterfaces)
                    {
                        if (iface.Ip_Address.Contains(entry.IpAddress) &&
                            string.IsNullOrWhiteSpace(iface.MAC))
                        {
                            iface.MAC = entry.MacAddress;
                            ProgressLog += "Updated MAC address.\r\n";
                        }
                    }
                }

                Thread.Sleep(10); // Optional: throttle processing
            }

            IsCompleted = true;
        }

        private List<ArpEntry> GetArpTable()
        {
            var result = new List<ArpEntry>();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"(?<ip>\d+\.\d+\.\d+\.\d+)\s+(?<mac>[a-fA-F0-9:-]{11,17})\s+(?<type>\w+)", RegexOptions.Compiled);

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var ip = match.Groups["ip"].Value.Trim();
                    var mac = match.Groups["mac"].Value.Trim().Replace("-", ":").ToUpper();
                    result.Add(new ArpEntry { IpAddress = ip, MacAddress = mac });
                }
            }

            return result;
        }

        private class ArpEntry
        {
            public string IpAddress { get; set; }
            public string MacAddress { get; set; }
        }
    }
}
