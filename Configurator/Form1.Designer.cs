using System.Text.Json;

namespace Configurator
{
    partial class RadioServiceConfigForm
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
            database_hostname = new TextBox();
            label1 = new Label();
            button_save = new Button();
            label2 = new Label();
            database_username = new TextBox();
            label3 = new Label();
            database_password = new TextBox();
            database_type = new ComboBox();
            label_database_type = new Label();
            button_close = new Button();
            test_connection_button = new Button();
            label_test_connection_status = new Label();
            database_name_label = new Label();
            database_name = new TextBox();
            database_schema_label = new Label();
            database_schema = new TextBox();
            save_message_label = new Label();
            tabControl1 = new TabControl();
            tabDatabaseConnection = new TabPage();
            tabGundiConnection = new TabPage();
            fetchGroupsButton = new Button();
            button_addGundiConnection = new Button();
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            tabControl1.SuspendLayout();
            tabDatabaseConnection.SuspendLayout();
            tabGundiConnection.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            // 
            // database_hostname
            // 
            database_hostname.Location = new Point(20, 97);
            database_hostname.Name = "database_hostname";
            database_hostname.Size = new Size(402, 23);
            database_hostname.TabIndex = 1;
            database_hostname.TextChanged += AnyControl_ValueChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(20, 77);
            label1.Name = "label1";
            label1.Size = new Size(113, 15);
            label1.TabIndex = 1;
            label1.Text = "Database Hostname";
            // 
            // button_save
            // 
            button_save.Location = new Point(742, 454);
            button_save.Name = "button_save";
            button_save.Size = new Size(75, 23);
            button_save.TabIndex = 6;
            button_save.Text = "Save";
            button_save.UseVisualStyleBackColor = true;
            button_save.Click += button_save_click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(20, 185);
            label2.Name = "label2";
            label2.Size = new Size(111, 15);
            label2.TabIndex = 3;
            label2.Text = "Database Username";
            // 
            // database_username
            // 
            database_username.Location = new Point(20, 210);
            database_username.Name = "database_username";
            database_username.Size = new Size(402, 23);
            database_username.TabIndex = 3;
            database_username.TextChanged += AnyControl_ValueChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(20, 246);
            label3.Name = "label3";
            label3.Size = new Size(108, 15);
            label3.TabIndex = 5;
            label3.Text = "Database Password";
            // 
            // database_password
            // 
            database_password.Location = new Point(20, 269);
            database_password.Name = "database_password";
            database_password.PasswordChar = '*';
            database_password.Size = new Size(402, 23);
            database_password.TabIndex = 4;
            database_password.TextChanged += AnyControl_ValueChanged;
            // 
            // database_type
            // 
            database_type.AutoCompleteMode = AutoCompleteMode.Suggest;
            database_type.AutoCompleteSource = AutoCompleteSource.ListItems;
            database_type.DropDownStyle = ComboBoxStyle.DropDownList;
            database_type.FormattingEnabled = true;
            database_type.Location = new Point(20, 44);
            database_type.Name = "database_type";
            database_type.Size = new Size(402, 23);
            database_type.TabIndex = 0;
            database_type.SelectedIndexChanged += database_type_SelectedIndexChanged;
            // 
            // label_database_type
            // 
            label_database_type.AutoSize = true;
            label_database_type.Location = new Point(20, 21);
            label_database_type.Name = "label_database_type";
            label_database_type.Size = new Size(82, 15);
            label_database_type.TabIndex = 7;
            label_database_type.Text = "Database Type";
            // 
            // button_close
            // 
            button_close.Location = new Point(661, 454);
            button_close.Name = "button_close";
            button_close.Size = new Size(75, 23);
            button_close.TabIndex = 7;
            button_close.Text = "Close";
            button_close.UseVisualStyleBackColor = true;
            button_close.Click += button_close_click;
            // 
            // test_connection_button
            // 
            test_connection_button.Location = new Point(20, 358);
            test_connection_button.Name = "test_connection_button";
            test_connection_button.Size = new Size(118, 23);
            test_connection_button.TabIndex = 5;
            test_connection_button.Text = "&Test Connecction";
            test_connection_button.UseVisualStyleBackColor = true;
            test_connection_button.Click += testConnectionButtonClick;
            // 
            // label_test_connection_status
            // 
            label_test_connection_status.AutoSize = true;
            label_test_connection_status.Location = new Point(20, 393);
            label_test_connection_status.Name = "label_test_connection_status";
            label_test_connection_status.Size = new Size(85, 15);
            label_test_connection_status.TabIndex = 13;
            label_test_connection_status.Text = "<placeholder>";
            label_test_connection_status.Visible = false;
            // 
            // database_name_label
            // 
            database_name_label.AutoSize = true;
            database_name_label.Location = new Point(20, 132);
            database_name_label.Name = "database_name_label";
            database_name_label.Size = new Size(90, 15);
            database_name_label.TabIndex = 14;
            database_name_label.Text = "Database Name";
            // 
            // database_name
            // 
            database_name.Location = new Point(20, 150);
            database_name.Name = "database_name";
            database_name.Size = new Size(402, 23);
            database_name.TabIndex = 2;
            database_name.TextChanged += AnyControl_ValueChanged;
            // 
            // database_schema_label
            // 
            database_schema_label.AutoSize = true;
            database_schema_label.Location = new Point(21, 302);
            database_schema_label.Name = "database_schema_label";
            database_schema_label.Size = new Size(100, 15);
            database_schema_label.TabIndex = 15;
            database_schema_label.Text = "Database Schema";
            // 
            // database_schema
            // 
            database_schema.Location = new Point(20, 320);
            database_schema.Name = "database_schema";
            database_schema.Size = new Size(402, 23);
            database_schema.TabIndex = 16;
            database_schema.TextChanged += AnyControl_ValueChanged;
            // 
            // save_message_label
            // 
            save_message_label.AutoSize = true;
            save_message_label.ForeColor = Color.Green;
            save_message_label.Location = new Point(742, 480);
            save_message_label.Name = "save_message_label";
            save_message_label.Size = new Size(41, 15);
            save_message_label.TabIndex = 17;
            save_message_label.Text = "Saved.";
            save_message_label.Visible = false;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabDatabaseConnection);
            tabControl1.Controls.Add(tabGundiConnection);
            tabControl1.Dock = DockStyle.Top;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(840, 448);
            tabControl1.TabIndex = 18;
            // 
            // tabDatabaseConnection
            // 
            tabDatabaseConnection.Controls.Add(label_database_type);
            tabDatabaseConnection.Controls.Add(database_hostname);
            tabDatabaseConnection.Controls.Add(database_schema);
            tabDatabaseConnection.Controls.Add(label1);
            tabDatabaseConnection.Controls.Add(database_schema_label);
            tabDatabaseConnection.Controls.Add(database_name);
            tabDatabaseConnection.Controls.Add(label2);
            tabDatabaseConnection.Controls.Add(database_name_label);
            tabDatabaseConnection.Controls.Add(database_username);
            tabDatabaseConnection.Controls.Add(label_test_connection_status);
            tabDatabaseConnection.Controls.Add(label3);
            tabDatabaseConnection.Controls.Add(test_connection_button);
            tabDatabaseConnection.Controls.Add(database_password);
            tabDatabaseConnection.Controls.Add(database_type);
            tabDatabaseConnection.Location = new Point(4, 24);
            tabDatabaseConnection.Name = "tabDatabaseConnection";
            tabDatabaseConnection.Padding = new Padding(3);
            tabDatabaseConnection.Size = new Size(832, 420);
            tabDatabaseConnection.TabIndex = 0;
            tabDatabaseConnection.Text = "Database Connection";
            tabDatabaseConnection.UseVisualStyleBackColor = true;
            tabDatabaseConnection.Click += tabDatabaseConnection_Click;
            // 
            // tabGundiConnection
            // 
            tabGundiConnection.AutoScroll = true;
            tabGundiConnection.Controls.Add(fetchGroupsButton);
            tabGundiConnection.Controls.Add(button_addGundiConnection);
            tabGundiConnection.Location = new Point(4, 24);
            tabGundiConnection.Name = "tabGundiConnection";
            tabGundiConnection.Padding = new Padding(3);
            tabGundiConnection.Size = new Size(832, 420);
            tabGundiConnection.TabIndex = 1;
            tabGundiConnection.Text = "Gundi Connections";
            tabGundiConnection.UseVisualStyleBackColor = true;
            // 
            // fetchGroupsButton
            // 
            fetchGroupsButton.Location = new Point(681, 39);
            fetchGroupsButton.Name = "fetchGroupsButton";
            fetchGroupsButton.Size = new Size(143, 23);
            fetchGroupsButton.TabIndex = 12;
            fetchGroupsButton.Text = "Refresh Groups";
            fetchGroupsButton.UseVisualStyleBackColor = true;
            fetchGroupsButton.Click += fetchGroupsButton_Click;
            // 
            // button_addGundiConnection
            // 
            button_addGundiConnection.FlatStyle = FlatStyle.System;
            button_addGundiConnection.Location = new Point(681, 6);
            button_addGundiConnection.Name = "button_addGundiConnection";
            button_addGundiConnection.Size = new Size(143, 23);
            button_addGundiConnection.TabIndex = 11;
            button_addGundiConnection.Text = "Add Connection";
            button_addGundiConnection.UseVisualStyleBackColor = true;
            button_addGundiConnection.Click += gundiConnectionsAddButtonClick;
            // 
            // statusStrip
            // 
            statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel });
            statusStrip.Location = new Point(0, 482);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(840, 22);
            statusStrip.TabIndex = 19;
            // 
            // statusLabel
            // 
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 17);
            // 
            // RadioServiceConfigForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(840, 504);
            Controls.Add(statusStrip);
            Controls.Add(tabControl1);
            Controls.Add(save_message_label);
            Controls.Add(button_save);
            Controls.Add(button_close);
            Name = "RadioServiceConfigForm";
            Text = "Radio Service Configuration";
            tabControl1.ResumeLayout(false);
            tabDatabaseConnection.ResumeLayout(false);
            tabDatabaseConnection.PerformLayout();
            tabGundiConnection.ResumeLayout(false);
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }
        #endregion

        private TabControl tabControl1;
        private TabPage tabDatabaseConnection;
        private TabPage tabGundiConnection;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private Button button_addGundiConnection;
        private Button fetchGroupsButton;
    }

}