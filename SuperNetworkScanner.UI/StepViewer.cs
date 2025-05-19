using SuperNetworkScanner.CollectionSteps;

namespace SuperNetworkScanner.UI
{
    public partial class StepViewer : Form
    {
        public List<ICollectionStep> CollectionSteps { get; }
        public int StepIndex { get; set; }  
        public StepViewer()
        {
            InitializeComponent();
            var hostViewer = new HostListViewer();
            hostViewer.Show();

            StepIndex = 0;
            CollectionSteps = new List<ICollectionStep>
            {
                new PingSweepStep()
            };
            IncrementStep();
        }

        private void IncrementStep()
        {
            if (StepIndex >= CollectionSteps.Count) return;
            StepIndex++;
            var step = CollectionSteps[StepIndex-1];
            lblStep.Text = $"Step {StepIndex} - {step.Name}";
            step.Start();

        }
    }
}
