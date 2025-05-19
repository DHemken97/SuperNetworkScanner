namespace SuperNetworkScanner.UI
{
    partial class StepViewer
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            lblStep = new Label();
            lblDescription = new Label();
            richTextBox1 = new RichTextBox();
            progressBar1 = new ProgressBar();
            lblProgressText = new Label();
            btnSkip = new Button();
            btnNext = new Button();
            timer1 = new System.Windows.Forms.Timer(components);
            SuspendLayout();
            // 
            // lblStep
            // 
            lblStep.AutoSize = true;
            lblStep.Font = new Font("Segoe UI", 35F);
            lblStep.Location = new Point(12, 9);
            lblStep.Name = "lblStep";
            lblStep.Size = new Size(157, 62);
            lblStep.TabIndex = 0;
            lblStep.Text = "Step 1";
            // 
            // lblDescription
            // 
            lblDescription.AutoSize = true;
            lblDescription.Location = new Point(24, 71);
            lblDescription.Name = "lblDescription";
            lblDescription.Size = new Size(66, 15);
            lblDescription.TabIndex = 1;
            lblDescription.Text = "description";
            // 
            // richTextBox1
            // 
            richTextBox1.Location = new Point(24, 89);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new Size(756, 290);
            richTextBox1.TabIndex = 2;
            richTextBox1.Text = "";
            // 
            // progressBar1
            // 
            progressBar1.Location = new Point(24, 392);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(756, 23);
            progressBar1.TabIndex = 3;
            // 
            // lblProgressText
            // 
            lblProgressText.AutoSize = true;
            lblProgressText.Location = new Point(386, 397);
            lblProgressText.Name = "lblProgressText";
            lblProgressText.Size = new Size(23, 15);
            lblProgressText.TabIndex = 4;
            lblProgressText.Text = "0%";
            // 
            // btnSkip
            // 
            btnSkip.Location = new Point(624, 421);
            btnSkip.Name = "btnSkip";
            btnSkip.Size = new Size(75, 23);
            btnSkip.TabIndex = 5;
            btnSkip.Text = "Skip";
            btnSkip.UseVisualStyleBackColor = true;
            btnSkip.Click += btnSkip_Click;
            // 
            // btnNext
            // 
            btnNext.Location = new Point(705, 421);
            btnNext.Name = "btnNext";
            btnNext.Size = new Size(75, 23);
            btnNext.TabIndex = 6;
            btnNext.Text = "Start";
            btnNext.UseVisualStyleBackColor = true;
            btnNext.Click += btnNext_Click;
            // 
            // timer1
            // 
            timer1.Enabled = true;
            timer1.Interval = 1000;
            timer1.Tick += timer1_Tick;
            // 
            // StepViewer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(btnNext);
            Controls.Add(btnSkip);
            Controls.Add(lblProgressText);
            Controls.Add(progressBar1);
            Controls.Add(richTextBox1);
            Controls.Add(lblDescription);
            Controls.Add(lblStep);
            Name = "StepViewer";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblStep;
        private Label lblDescription;
        private RichTextBox richTextBox1;
        private ProgressBar progressBar1;
        private Label lblProgressText;
        private Button btnSkip;
        private Button btnNext;
        private System.Windows.Forms.Timer timer1;
    }
}
