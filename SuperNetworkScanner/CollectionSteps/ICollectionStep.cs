using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperNetworkScanner.CollectionSteps
{
    public interface ICollectionStep
    {
        public string Name { get; }
        public string Description { get; }
        public string ProgressMessage { get; }
        public string ProgressLog { get; }
        public decimal ProgressPercentage { get; }
        public bool IsCompleted { get; }
        public void Start(List<string> search_ips);
    }
}
