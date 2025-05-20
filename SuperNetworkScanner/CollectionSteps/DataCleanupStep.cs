using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SuperNetworkScanner.Models;

namespace SuperNetworkScanner.CollectionSteps
{
    public class DataCleanupStep : ICollectionStep
    {
        public string Name => "Data Cleanup";

        public string Description => "One More Check through the collected data to infer values";

        public string ProgressMessage { get; set; }

        public string ProgressLog => "";

        public decimal ProgressPercentage { get; set; }

        public bool IsCompleted { get; set; }

        public void Start(List<string> search_ips)
        {

            decimal i = 0;
            decimal count = NetworkMap.Hosts.Count;
            foreach (var host in NetworkMap.Hosts)
            {
                i++;
                UpdateHostFields(host);
                ProgressPercentage = i / count;
            }
            IsCompleted = true;

        }
        private void UpdateHostFields(Host host)
        {
            var allServices = host.NetworkInterfaces.SelectMany(x => x.Services).ToList();
            var allData = JsonConvert.SerializeObject(host);
            ProgressMessage = $"Checking {host.ToString()}";

            //Windows
            if (allServices.Any(x => x.Port == 135) ||
           new[] {
                "windows",
                "iis",
                "microsoft"
           }.Any(x => allData.ToLower().Contains(x.ToLower()))
           )
            {
                host.OperatingSystem = "Windows";
                
            }

            //Linux

            var linuxDistros = new List<string>
                {
                    "Debian",
                    "Alma",
                    "CentOS"
                };
            var distro = linuxDistros.FirstOrDefault(x => allData.ToLower().Contains(x.ToLower()));

            if (
           new[] {
                "linux",
           }.Any(x => allData.ToLower().Contains(x.ToLower()))
           || !string.IsNullOrWhiteSpace(distro)
           )
            {
                host.OperatingSystem = "Linux";

               


                if (!string.IsNullOrWhiteSpace(distro))
                    host.OperatingSystemVersion = distro;



            }


            //Cisco IOS
            if (
           new[] {
                "cisco",
                " ios "
           }.Any(x => allData.ToLower().Contains(x.ToLower()))
           )
            {
                host.OperatingSystem = "Cisco IOS";
                host.Manufacturer = "Cisco";
            }


            //HP
            if (
           new[] {
                "hp",
           }.Any(x => allData.ToLower().Contains(x.ToLower()))
           )
            {
                host.Manufacturer = "HP";
            }





        }
    }
}
