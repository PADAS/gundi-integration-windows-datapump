using System.Text.Json;

namespace Configurator
{
    partial class service_config_form
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
            panel_identifier_pairs = new Panel();
            button_add_id_pair = new Button();
            group_id_map_label = new Label();
            test_connection_button = new Button();
            test_connection_status_lablel = new Label();
            database_name_label = new Label();
            database_name = new TextBox();
            database_schema_label = new Label();
            database_schema = new TextBox();
            SuspendLayout();
            // 
            // database_hostname
            // 
            database_hostname.Location = new Point(47, 105);
            database_hostname.Name = "database_hostname";
            database_hostname.Size = new Size(266, 23);
            database_hostname.TabIndex = 1;
            database_hostname.TextChanged += AnyControl_ValueChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(47, 85);
            label1.Name = "label1";
            label1.Size = new Size(113, 15);
            label1.TabIndex = 1;
            label1.Text = "Database Hostname";
            // 
            // button_save
            // 
            button_save.Location = new Point(721, 407);
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
            label2.Location = new Point(47, 193);
            label2.Name = "label2";
            label2.Size = new Size(111, 15);
            label2.TabIndex = 3;
            label2.Text = "Database Username";
            // 
            // database_username
            // 
            database_username.Location = new Point(47, 218);
            database_username.Name = "database_username";
            database_username.Size = new Size(266, 23);
            database_username.TabIndex = 3;
            database_username.TextChanged += AnyControl_ValueChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(47, 254);
            label3.Name = "label3";
            label3.Size = new Size(108, 15);
            label3.TabIndex = 5;
            label3.Text = "Database Password";
            // 
            // database_password
            // 
            database_password.Location = new Point(47, 277);
            database_password.Name = "database_password";
            database_password.PasswordChar = '*';
            database_password.Size = new Size(266, 23);
            database_password.TabIndex = 4;
            database_password.TextChanged += AnyControl_ValueChanged;
            // 
            // database_type
            // 
            database_type.AutoCompleteMode = AutoCompleteMode.Suggest;
            database_type.AutoCompleteSource = AutoCompleteSource.ListItems;
            database_type.DropDownStyle = ComboBoxStyle.DropDownList;
            database_type.FormattingEnabled = true;
            database_type.Items.AddRange(new object[] { "Smart Dispatch Plus", "Smart One Dispatch", "TRBOnet", "Kenwood KAS20" });
            database_type.Location = new Point(47, 52);
            database_type.Name = "database_type";
            database_type.Size = new Size(266, 23);
            database_type.TabIndex = 0;
            database_type.SelectedIndexChanged += AnyControl_ValueChanged;
            database_type.SelectedIndexChanged += database_type_SelectedIndexChanged;
            // 
            // label_database_type
            // 
            label_database_type.AutoSize = true;
            label_database_type.Location = new Point(47, 29);
            label_database_type.Name = "label_database_type";
            label_database_type.Size = new Size(82, 15);
            label_database_type.TabIndex = 7;
            label_database_type.Text = "Database Type";
            // 
            // button_close
            // 
            button_close.Location = new Point(640, 407);
            button_close.Name = "button_close";
            button_close.Size = new Size(75, 23);
            button_close.TabIndex = 7;
            button_close.Text = "Close";
            button_close.UseVisualStyleBackColor = true;
            button_close.Click += button_close_click;
            // 
            // panel_identifier_pairs
            // 
            panel_identifier_pairs.AutoScroll = true;
            panel_identifier_pairs.BorderStyle = BorderStyle.FixedSingle;
            panel_identifier_pairs.Location = new Point(356, 52);
            panel_identifier_pairs.Name = "panel_identifier_pairs";
            panel_identifier_pairs.Size = new Size(440, 216);
            panel_identifier_pairs.TabIndex = 9;
            // 
            // button_add_id_pair
            // 
            button_add_id_pair.Location = new Point(711, 284);
            button_add_id_pair.Name = "button_add_id_pair";
            button_add_id_pair.Size = new Size(85, 23);
            button_add_id_pair.TabIndex = 10;
            button_add_id_pair.Text = "Add";
            button_add_id_pair.UseVisualStyleBackColor = true;
            button_add_id_pair.Click += button_add_id_pair_click;
            // 
            // group_id_map_label
            // 
            group_id_map_label.AutoSize = true;
            group_id_map_label.Location = new Point(356, 29);
            group_id_map_label.Name = "group_id_map_label";
            group_id_map_label.Size = new Size(81, 15);
            group_id_map_label.TabIndex = 11;
            group_id_map_label.Text = "Group ID Map";
            // 
            // test_connection_button
            // 
            test_connection_button.Location = new Point(47, 366);
            test_connection_button.Name = "test_connection_button";
            test_connection_button.Size = new Size(118, 23);
            test_connection_button.TabIndex = 5;
            test_connection_button.Text = "&Test Connecction";
            test_connection_button.UseVisualStyleBackColor = true;
            test_connection_button.Click += test_connection_button_Click;
            // 
            // test_connection_status_lablel
            // 
            test_connection_status_lablel.AutoSize = true;
            test_connection_status_lablel.Location = new Point(47, 401);
            test_connection_status_lablel.Name = "test_connection_status_lablel";
            test_connection_status_lablel.Size = new Size(85, 15);
            test_connection_status_lablel.TabIndex = 13;
            test_connection_status_lablel.Text = "<placeholder>";
            test_connection_status_lablel.Visible = false;
            // 
            // database_name_label
            // 
            database_name_label.AutoSize = true;
            database_name_label.Location = new Point(47, 140);
            database_name_label.Name = "database_name_label";
            database_name_label.Size = new Size(90, 15);
            database_name_label.TabIndex = 14;
            database_name_label.Text = "Database Name";
            // 
            // database_name
            // 
            database_name.Location = new Point(47, 158);
            database_name.Name = "database_name";
            database_name.Size = new Size(267, 23);
            database_name.TabIndex = 2;
            database_name.TextChanged += AnyControl_ValueChanged;
            // 
            // database_schema_label
            // 
            database_schema_label.AutoSize = true;
            database_schema_label.Location = new Point(48, 310);
            database_schema_label.Name = "database_schema_label";
            database_schema_label.Size = new Size(100, 15);
            database_schema_label.TabIndex = 15;
            database_schema_label.Text = "Database Schema";
            // 
            // database_schema
            // 
            database_schema.Location = new Point(47, 328);
            database_schema.Name = "database_schema";
            database_schema.Size = new Size(267, 23);
            database_schema.TabIndex = 16;
            database_schema.TextChanged += AnyControl_ValueChanged;
            // 
            // service_config_form
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(815, 450);
            Controls.Add(database_schema);
            Controls.Add(database_schema_label);
            Controls.Add(database_name);
            Controls.Add(database_name_label);
            Controls.Add(test_connection_status_lablel);
            Controls.Add(test_connection_button);
            Controls.Add(group_id_map_label);
            Controls.Add(button_add_id_pair);
            Controls.Add(panel_identifier_pairs);
            Controls.Add(button_close);
            Controls.Add(label_database_type);
            Controls.Add(database_type);
            Controls.Add(database_password);
            Controls.Add(label3);
            Controls.Add(database_username);
            Controls.Add(label2);
            Controls.Add(button_save);
            Controls.Add(label1);
            Controls.Add(database_hostname);
            Name = "service_config_form";
            Text = "Radio Service Configuration";
            ResumeLayout(false);
            PerformLayout();
        }
        #endregion

        private void SaveToJson(string hostname, string username, string password,
            string database_type, string database_name, string database_schema,
            List<GroupKeyPair> groups)
        {
            // Create a JSON object
            var config = new
            {
                Hostname = hostname,
                Username = username,
                Password = password,
                DatabaseType = database_type,
                DatabaseName = database_name,
                DatabaseSchema = database_schema,
                Groups = groups


            };

            // Serialize to JSON
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

            // Write to a file
            string filePath = "config.json";
            File.WriteAllText(filePath, json);

            MessageBox.Show("Configuration saved to " + filePath, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LoadFromJson()
        {
            string filePath = "config.json";

            // Check if the file exists
            if (File.Exists(filePath))
            {
                try
                {
                    // Read from the JSON file
                    string json = File.ReadAllText(filePath);

                    // Deserialize the JSON
                    var config = JsonSerializer.Deserialize<Config>(json);

                    // Populate form controls
                    database_hostname.Text = config?.Hostname ?? string.Empty;
                    database_username.Text = config?.Username ?? string.Empty;
                    database_password.Text = config?.Password ?? string.Empty;
                    database_name.Text = config?.DatabaseName ?? string.Empty;
                    database_schema.Text = config?.DatabaseSchema ?? string.Empty;

                    // Set the selected item for the ComboBox
                    if (!string.IsNullOrEmpty(config?.DatabaseType) && database_type.Items.Contains(config.DatabaseType))
                    {
                        database_type.SelectedItem = config.DatabaseType;
                    }

                    //MessageBox.Show("Configuration loaded from " + filePath, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading configuration: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                //MessageBox.Show("Configuration file not found.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }


        private TextBox database_hostname;
        private Label label1;
        private Button button_save;
        private Label label2;
        private TextBox database_username;
        private Label label3;
        private TextBox database_password;
        private ComboBox database_type;
        private Label label_database_type;
        private Button button_close;
        private Panel panel_identifier_pairs;
        private Button button_add_id_pair;
        private List<PairTextBox> identifierPairs;
        private Label group_id_map_label;
        private Button test_connection_button;
        private Label test_connection_status_lablel;
        private Label database_name_label;
        private TextBox database_name;
        private Label database_schema_label;
        private TextBox database_schema;
    }

    public class Config
    {
        public string Hostname { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string DatabaseName { get; set; }
        public string DatabaseType { get; set; }
        public string DatabaseSchema { get; set; }
        public List<GroupKeyPair> Groups { get; set; }
    }

    public class PairTextBox
    {
        public Label GroupAliasLabel { get; set; }
        public TextBox GroupAliasTextBox { get; set; }
        public Label GundiApiKeyLabel { get; set; }
        public TextBox GundiApiKeyTextBox { get; set; }
        public Button DeletePairButton { get; set; }
        private int top_offset;
        public int TopOffset
        {
            get { return top_offset; }

            set
            {
                top_offset = value;
                GroupAliasLabel.Top = top_offset;
                GroupAliasTextBox.Top = top_offset;
                GundiApiKeyLabel.Top = top_offset + 30;
                GundiApiKeyTextBox.Top = top_offset + 30;
                DeletePairButton.Top = top_offset;
            }
        }

        public PairTextBox(int index)
        {
            var top_offset = index * 60;
            // Initialize the source identifier label
            GroupAliasLabel = new Label()
            {
                Text = "Group Alias",
                Top = top_offset,
                Left = 10,
                Width = 100
            };

            // Initialize the source identifier textbox
            GroupAliasTextBox = new TextBox()
            {
                Top = top_offset,
                Left = 130,
                Width = 160
            };

            // Initialize the destination identifier label
            GundiApiKeyLabel = new Label()
            {
                Text = "Gundi API Key",
                Top = top_offset + 30,
                Left = 10,
                Width = 100
            };

            // Initialize the destination identifier textbox
            GundiApiKeyTextBox = new TextBox()
            {
                Top = top_offset + 30,
                Left = 130,
                Width = 160
            };

            DeletePairButton = new Button()
            {
                Text = "Remove",
                Top = top_offset,
                Left = 300,
                Width = 90
            };

            DeletePairButton.Click += (sender, e) =>
            {
                GroupAliasLabel.Dispose();
                GroupAliasTextBox.Dispose();
                GundiApiKeyLabel.Dispose();
                GundiApiKeyTextBox.Dispose();
                DeletePairButton.Dispose();
            };


        }

    }
}