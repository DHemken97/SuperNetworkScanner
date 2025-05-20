using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperNetworkScanner.CollectionSteps
{
    public class FinishedStep : ICollectionStep
    {
        public string Name => "Finished";

        public string Description => "Refresh the host viewing window";

        public string ProgressMessage => "";

        public string ProgressLog => "";

        public decimal ProgressPercentage => 1;

        public bool IsCompleted => false;

        public void Start(List<string> search_ips)
        {
        }
    }
}
