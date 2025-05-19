using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperNetworkScanner.CollectionSteps
{
    public class PingSweepStep : ICollectionStep
    {
        public string Name => "Ping Sweep";

        public string Description => "Ping a range of IPs to see what responds";

        public string ProgressMessage { get; private set; }

        public string ProgressLog { get; private set; }

        public decimal ProgressPercentage { get; private set; }

        public bool IsCompleted { get; private set; }

        public void Start()
        {
        }
    }
}
