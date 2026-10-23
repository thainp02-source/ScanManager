namespace ScanManager
{
    partial class PageManagerForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            btnInsertPage = new Button();
            btnDelete = new Button();
            btnMoveUp = new Button();
            btnMoveDown = new Button();
            btnRotate = new Button();
            btnClose = new Button();
            lvPages = new ListView();
            imageList1 = new ImageList(components);
            picPreview = new PictureBox();
            btnMergePdf = new Button();
            ((System.ComponentModel.ISupportInitialize)picPreview).BeginInit();
            SuspendLayout();
            // 
            // btnInsertPage
            // 
            btnInsertPage.Location = new Point(652, 184);
            btnInsertPage.Name = "btnInsertPage";
            btnInsertPage.Size = new Size(75, 23);
            btnInsertPage.TabIndex = 1;
            btnInsertPage.Text = "Chèn";
            btnInsertPage.UseVisualStyleBackColor = true;
            btnInsertPage.Click += btnInsertPage_Click;
            // 
            // btnDelete
            // 
            btnDelete.Location = new Point(652, 29);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new Size(75, 23);
            btnDelete.TabIndex = 2;
            btnDelete.Text = "Xóa";
            btnDelete.UseVisualStyleBackColor = true;
            btnDelete.Click += btnDelete_Click;
            // 
            // btnMoveUp
            // 
            btnMoveUp.Location = new Point(652, 69);
            btnMoveUp.Name = "btnMoveUp";
            btnMoveUp.Size = new Size(75, 23);
            btnMoveUp.TabIndex = 3;
            btnMoveUp.Text = "↑";
            btnMoveUp.UseVisualStyleBackColor = true;
            btnMoveUp.Click += btnMoveUp_Click;
            // 
            // btnMoveDown
            // 
            btnMoveDown.Location = new Point(652, 107);
            btnMoveDown.Name = "btnMoveDown";
            btnMoveDown.Size = new Size(75, 23);
            btnMoveDown.TabIndex = 4;
            btnMoveDown.Text = "↓";
            btnMoveDown.UseVisualStyleBackColor = true;
            btnMoveDown.Click += btnMoveDown_Click;
            // 
            // btnRotate
            // 
            btnRotate.Location = new Point(652, 145);
            btnRotate.Name = "btnRotate";
            btnRotate.Size = new Size(75, 23);
            btnRotate.TabIndex = 5;
            btnRotate.Text = "Xoay";
            btnRotate.UseVisualStyleBackColor = true;
            btnRotate.Click += btnRotate_Click;
            // 
            // btnClose
            // 
            btnClose.Location = new Point(652, 585);
            btnClose.Name = "btnClose";
            btnClose.Size = new Size(75, 23);
            btnClose.TabIndex = 6;
            btnClose.Text = "Đóng";
            btnClose.UseVisualStyleBackColor = true;
            btnClose.Click += btnClose_Click;
            // 
            // lvPages
            // 
            lvPages.LargeImageList = imageList1;
            lvPages.Location = new Point(21, 5);
            lvPages.Name = "lvPages";
            lvPages.Size = new Size(129, 603);
            lvPages.TabIndex = 7;
            lvPages.UseCompatibleStateImageBehavior = false;
            lvPages.SelectedIndexChanged += lvPages_SelectedIndexChanged;
            // 
            // imageList1
            // 
            imageList1.ColorDepth = ColorDepth.Depth32Bit;
            imageList1.ImageSize = new Size(120, 120);
            imageList1.TransparentColor = Color.Transparent;
            // 
            // picPreview
            // 
            picPreview.BorderStyle = BorderStyle.FixedSingle;
            picPreview.Location = new Point(156, 5);
            picPreview.Name = "picPreview";
            picPreview.Size = new Size(474, 603);
            picPreview.SizeMode = PictureBoxSizeMode.Zoom;
            picPreview.TabIndex = 8;
            picPreview.TabStop = false;
            picPreview.Click += picPreview_Click;
            // 
            // btnMergePdf
            // 
            btnMergePdf.Location = new Point(652, 224);
            btnMergePdf.Name = "btnMergePdf";
            btnMergePdf.Size = new Size(75, 23);
            btnMergePdf.TabIndex = 9;
            btnMergePdf.Text = "Ghép PDF";
            btnMergePdf.UseVisualStyleBackColor = true;
            btnMergePdf.Click += btnMergePdf_Click;
            // 
            // PageManagerForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 656);
            Controls.Add(btnMergePdf);
            Controls.Add(picPreview);
            Controls.Add(lvPages);
            Controls.Add(btnClose);
            Controls.Add(btnRotate);
            Controls.Add(btnMoveDown);
            Controls.Add(btnMoveUp);
            Controls.Add(btnDelete);
            Controls.Add(btnInsertPage);
            Name = "PageManagerForm";
            Text = "PageManagerForm";
            ((System.ComponentModel.ISupportInitialize)picPreview).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private Button btnInsertPage;
        private Button btnDelete;
        private Button btnMoveUp;
        private Button btnMoveDown;
        private Button btnRotate;
        private Button btnClose;
        private ListView lvPages;
        private ImageList imageList1;
        private PictureBox picPreview;
        private Button btnMergePdf;
    }
}