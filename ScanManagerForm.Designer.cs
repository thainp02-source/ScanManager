namespace ScanManager
{
    partial class ScanManagerForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ScanManagerForm));
            label1 = new Label();
            label2 = new Label();
            txtFileName = new TextBox();
            txtFolder = new TextBox();
            btnBrowse = new Button();
            btnScan = new Button();
            label3 = new Label();
            dgvDocuments = new DataGridView();
            btnScanPage = new Button();
            btnFinishPdf = new Button();
            label4 = new Label();
            cmbFileType = new ComboBox();
            contextMenuStrip1 = new ContextMenuStrip(components);
            mởTàiLiệuToolStripMenuItem = new ToolStripMenuItem();
            xóaToolStripMenuItem = new ToolStripMenuItem();
            inLạiToolStripMenuItem = new ToolStripMenuItem();
            mởThưMụcChứaFileToolStripMenuItem = new ToolStripMenuItem();
            txtDocumentType = new TextBox();
            btnManagePages = new Button();
            lblScanner = new Label();
            cmbScanner = new ComboBox();
            btnRefreshScanner = new Button();
            lblDpi = new Label();
            cmbDpi = new ComboBox();
            lblScanSettings = new Label();
            lblScanSource = new Label();
            cmbScanSource = new ComboBox();
            lblColorMode = new Label();
            cmbColorMode = new ComboBox();
            chkDuplex = new CheckBox();
            chkContinuousScan = new CheckBox();
            btnStopScan = new Button();
            lblDocumentHistory = new Label();
            chkOnePageOneFile = new CheckBox();
            ((System.ComponentModel.ISupportInitialize)dgvDocuments).BeginInit();
            contextMenuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(label1, "label1");
            label1.Name = "label1";
            // 
            // label2
            // 
            resources.ApplyResources(label2, "label2");
            label2.Name = "label2";
            // 
            // txtFileName
            // 
            resources.ApplyResources(txtFileName, "txtFileName");
            txtFileName.Name = "txtFileName";
            // 
            // txtFolder
            // 
            resources.ApplyResources(txtFolder, "txtFolder");
            txtFolder.Name = "txtFolder";
            // 
            // btnBrowse
            // 
            resources.ApplyResources(btnBrowse, "btnBrowse");
            btnBrowse.Name = "btnBrowse";
            btnBrowse.UseVisualStyleBackColor = true;
            btnBrowse.Click += btnBrowse_Click;
            // 
            // btnScan
            // 
            resources.ApplyResources(btnScan, "btnScan");
            btnScan.AllowDrop = true;
            btnScan.BackColor = Color.FromArgb(31, 41, 55);
            btnScan.Cursor = Cursors.Hand;
            btnScan.DialogResult = DialogResult.Ignore;
            btnScan.ForeColor = Color.White;
            btnScan.Name = "btnScan";
            btnScan.UseVisualStyleBackColor = false;
            btnScan.Click += btnScan_Click;
            // 
            // label3
            // 
            resources.ApplyResources(label3, "label3");
            label3.Name = "label3";
            label3.Click += label3_Click;
            // 
            // dgvDocuments
            // 
            resources.ApplyResources(dgvDocuments, "dgvDocuments");
            dgvDocuments.AllowUserToAddRows = false;
            dgvDocuments.AllowUserToDeleteRows = false;
            dgvDocuments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDocuments.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvDocuments.Name = "dgvDocuments";
            dgvDocuments.RowHeadersVisible = false;
            dgvDocuments.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDocuments.CellContentClick += dgvDocuments_CellContentClick;
            // 
            // btnScanPage
            // 
            resources.ApplyResources(btnScanPage, "btnScanPage");
            btnScanPage.Name = "btnScanPage";
            btnScanPage.UseVisualStyleBackColor = true;
            btnScanPage.Click += btnScanPage_Click;
            // 
            // btnFinishPdf
            // 
            resources.ApplyResources(btnFinishPdf, "btnFinishPdf");
            btnFinishPdf.Name = "btnFinishPdf";
            btnFinishPdf.UseVisualStyleBackColor = true;
            btnFinishPdf.Click += btnFinishPdf_Click;
            // 
            // label4
            // 
            resources.ApplyResources(label4, "label4");
            label4.Name = "label4";
            // 
            // cmbFileType
            // 
            resources.ApplyResources(cmbFileType, "cmbFileType");
            cmbFileType.FormattingEnabled = true;
            cmbFileType.Name = "cmbFileType";
            // 
            // contextMenuStrip1
            // 
            resources.ApplyResources(contextMenuStrip1, "contextMenuStrip1");
            contextMenuStrip1.Items.AddRange(new ToolStripItem[] { mởTàiLiệuToolStripMenuItem, xóaToolStripMenuItem, inLạiToolStripMenuItem, mởThưMụcChứaFileToolStripMenuItem });
            contextMenuStrip1.Name = "contextMenuStrip1";
            // 
            // mởTàiLiệuToolStripMenuItem
            // 
            resources.ApplyResources(mởTàiLiệuToolStripMenuItem, "mởTàiLiệuToolStripMenuItem");
            mởTàiLiệuToolStripMenuItem.Name = "mởTàiLiệuToolStripMenuItem";
            mởTàiLiệuToolStripMenuItem.Click += mởTàiLiệuToolStripMenuItem_Click;
            // 
            // xóaToolStripMenuItem
            // 
            resources.ApplyResources(xóaToolStripMenuItem, "xóaToolStripMenuItem");
            xóaToolStripMenuItem.Name = "xóaToolStripMenuItem";
            xóaToolStripMenuItem.Click += xóaToolStripMenuItem_Click;
            // 
            // inLạiToolStripMenuItem
            // 
            resources.ApplyResources(inLạiToolStripMenuItem, "inLạiToolStripMenuItem");
            inLạiToolStripMenuItem.Name = "inLạiToolStripMenuItem";
            inLạiToolStripMenuItem.Click += inLạiToolStripMenuItem_Click;
            // 
            // mởThưMụcChứaFileToolStripMenuItem
            // 
            resources.ApplyResources(mởThưMụcChứaFileToolStripMenuItem, "mởThưMụcChứaFileToolStripMenuItem");
            mởThưMụcChứaFileToolStripMenuItem.Name = "mởThưMụcChứaFileToolStripMenuItem";
            mởThưMụcChứaFileToolStripMenuItem.Click += mởThưMụcChứaFileToolStripMenuItem_Click;
            // 
            // txtDocumentType
            // 
            resources.ApplyResources(txtDocumentType, "txtDocumentType");
            txtDocumentType.Name = "txtDocumentType";
            txtDocumentType.TextChanged += txtDocumentType_TextChanged;
            // 
            // btnManagePages
            // 
            resources.ApplyResources(btnManagePages, "btnManagePages");
            btnManagePages.Name = "btnManagePages";
            btnManagePages.UseVisualStyleBackColor = true;
            btnManagePages.Click += btnManagePages_Click;
            // 
            // lblScanner
            // 
            resources.ApplyResources(lblScanner, "lblScanner");
            lblScanner.Name = "lblScanner";
            lblScanner.Click += lblScanner_Click;
            // 
            // cmbScanner
            // 
            resources.ApplyResources(cmbScanner, "cmbScanner");
            cmbScanner.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbScanner.FormattingEnabled = true;
            cmbScanner.Name = "cmbScanner";
            // 
            // btnRefreshScanner
            // 
            resources.ApplyResources(btnRefreshScanner, "btnRefreshScanner");
            btnRefreshScanner.Name = "btnRefreshScanner";
            btnRefreshScanner.UseVisualStyleBackColor = true;
            // 
            // lblDpi
            // 
            resources.ApplyResources(lblDpi, "lblDpi");
            lblDpi.Name = "lblDpi";
            lblDpi.Click += lblDpi_Click;
            // 
            // cmbDpi
            // 
            resources.ApplyResources(cmbDpi, "cmbDpi");
            cmbDpi.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDpi.FormattingEnabled = true;
            cmbDpi.Items.AddRange(new object[] { resources.GetString("cmbDpi.Items"), resources.GetString("cmbDpi.Items1"), resources.GetString("cmbDpi.Items2") });
            cmbDpi.Name = "cmbDpi";
            // 
            // lblScanSettings
            // 
            resources.ApplyResources(lblScanSettings, "lblScanSettings");
            lblScanSettings.BackColor = Color.Transparent;
            lblScanSettings.Name = "lblScanSettings";
            // 
            // lblScanSource
            // 
            resources.ApplyResources(lblScanSource, "lblScanSource");
            lblScanSource.Name = "lblScanSource";
            // 
            // cmbScanSource
            // 
            resources.ApplyResources(cmbScanSource, "cmbScanSource");
            cmbScanSource.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbScanSource.FormattingEnabled = true;
            cmbScanSource.Items.AddRange(new object[] { resources.GetString("cmbScanSource.Items"), resources.GetString("cmbScanSource.Items1") });
            cmbScanSource.Name = "cmbScanSource";
            // 
            // lblColorMode
            // 
            resources.ApplyResources(lblColorMode, "lblColorMode");
            lblColorMode.Name = "lblColorMode";
            // 
            // cmbColorMode
            // 
            resources.ApplyResources(cmbColorMode, "cmbColorMode");
            cmbColorMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbColorMode.FormattingEnabled = true;
            cmbColorMode.Items.AddRange(new object[] { resources.GetString("cmbColorMode.Items"), resources.GetString("cmbColorMode.Items1"), resources.GetString("cmbColorMode.Items2") });
            cmbColorMode.Name = "cmbColorMode";
            // 
            // chkDuplex
            // 
            resources.ApplyResources(chkDuplex, "chkDuplex");
            chkDuplex.Name = "chkDuplex";
            chkDuplex.UseVisualStyleBackColor = true;
            // 
            // chkContinuousScan
            // 
            resources.ApplyResources(chkContinuousScan, "chkContinuousScan");
            chkContinuousScan.Checked = true;
            chkContinuousScan.CheckState = CheckState.Checked;
            chkContinuousScan.Name = "chkContinuousScan";
            chkContinuousScan.UseVisualStyleBackColor = true;
            // 
            // btnStopScan
            // 
            resources.ApplyResources(btnStopScan, "btnStopScan");
            btnStopScan.Cursor = Cursors.Hand;
            btnStopScan.Name = "btnStopScan";
            btnStopScan.UseVisualStyleBackColor = true;
            // 
            // lblDocumentHistory
            // 
            resources.ApplyResources(lblDocumentHistory, "lblDocumentHistory");
            lblDocumentHistory.Name = "lblDocumentHistory";
            // 
            // chkOnePageOneFile
            // 
            resources.ApplyResources(chkOnePageOneFile, "chkOnePageOneFile");
            chkOnePageOneFile.Name = "chkOnePageOneFile";
            chkOnePageOneFile.UseVisualStyleBackColor = true;
            // 
            // ScanManagerForm
            // 
            resources.ApplyResources(this, "$this");
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(chkOnePageOneFile);
            Controls.Add(lblDocumentHistory);
            Controls.Add(btnStopScan);
            Controls.Add(chkContinuousScan);
            Controls.Add(chkDuplex);
            Controls.Add(cmbColorMode);
            Controls.Add(lblColorMode);
            Controls.Add(cmbScanSource);
            Controls.Add(lblScanSource);
            Controls.Add(lblScanSettings);
            Controls.Add(cmbDpi);
            Controls.Add(lblDpi);
            Controls.Add(btnRefreshScanner);
            Controls.Add(cmbScanner);
            Controls.Add(lblScanner);
            Controls.Add(btnManagePages);
            Controls.Add(txtDocumentType);
            Controls.Add(cmbFileType);
            Controls.Add(label4);
            Controls.Add(btnFinishPdf);
            Controls.Add(btnScanPage);
            Controls.Add(dgvDocuments);
            Controls.Add(label3);
            Controls.Add(btnScan);
            Controls.Add(btnBrowse);
            Controls.Add(txtFolder);
            Controls.Add(txtFileName);
            Controls.Add(label2);
            Controls.Add(label1);
            ForeColor = Color.FromArgb(35, 43, 55);
            Name = "ScanManagerForm";
            ((System.ComponentModel.ISupportInitialize)dgvDocuments).EndInit();
            contextMenuStrip1.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label label2;
        private TextBox txtFileName;
        private TextBox txtFolder;
        private Button btnBrowse;
        private Button btnScan;
        private Label label3;
        private DataGridView dgvDocuments;
        private Button btnScanPage;
        private Button btnFinishPdf;
        private Label label4;
        private ComboBox cmbFileType;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem mởTàiLiệuToolStripMenuItem;
        private ToolStripMenuItem xóaToolStripMenuItem;
        private ToolStripMenuItem inLạiToolStripMenuItem;
        private ToolStripMenuItem mởThưMụcChứaFileToolStripMenuItem;
        private TextBox txtDocumentType;
        private Button btnManagePages;
        private Label lblScanner;
        private ComboBox cmbScanner;
        private Button btnRefreshScanner;
        private Label lblDpi;
        private ComboBox cmbDpi;
        private Label lblScanSettings;
        private Label lblScanSource;
        private ComboBox cmbScanSource;
        private Label lblColorMode;
        private ComboBox cmbColorMode;
        private CheckBox chkDuplex;
        private CheckBox chkContinuousScan;
        private Button btnStopScan;
        private Label lblDocumentHistory;
        private CheckBox chkOnePageOneFile;
    }
}