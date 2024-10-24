using System.Text.Json;
using DataPump;

namespace Configurator
{
    public partial class RadioServiceConfigForm : Form
    {
        public RadioServiceConfigForm()
        {
            identifierPairs = new List<GroupKeyPairControl>();
            InitializeComponent();
            LoadFromJson();
        }

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

                    // Populate the identifier pairs
                    config.Groups.ForEach(item =>
                    {
                        var cfg = new GroupKeyPair { api_key = item.api_key, group_alias = item.group_alias, group_guid = item.group_guid };
                        add_group_alias(this, EventArgs.Empty, cfg);
                    });


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
        private List<GroupKeyPairControl> identifierPairs;
        private Label group_id_map_label;
        private Button test_connection_button;
        private Label label_test_connection_status;
        private Label database_name_label;
        private TextBox database_name;
        private Label database_schema_label;
        private TextBox database_schema;
        private Label save_message_label;

        private void button_save_click(object sender, EventArgs e)
        {

            var groupKeyPairs = new List<GroupKeyPair>();
            this.identifierPairs.ForEach(pair =>
            {
                var gp = new GroupKeyPair
                {
                    group_guid = "",
                    group_alias = pair.GroupAliasTextBox.Text,
                    api_key = pair.GundiApiKeyTextBox.Text
                };
                groupKeyPairs.Add(gp);

            });
            SaveToJson(this.database_hostname.Text,
                this.database_username.Text, this.database_password.Text,
                this.database_type.Text, this.database_name.Text,
                this.database_schema.Text,
                groupKeyPairs
                );

        }

        private void button_close_click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void render_identifier_pairs(object sender, EventArgs e)
        {
            //panel_identifier_pairs.Controls.Clear();

            var index = 0;
            identifierPairs.ForEach(pair =>
            {
                pair.TopOffset = 5 + index * 60;
                index = index + 1;
            });
            // Adjust panel height dynamically
            //panel_identifier_pairs.Height = Math.Min(100 + (identifierPairs.Count * 50), 300);
        }
        private void button_add_id_pair_click(object sender, EventArgs e)
        {
            // Create a new identifier pair
            var present_count = identifierPairs.Count;
            var pair = new GroupKeyPairControl(present_count);
            identifierPairs.Add(pair);

            panel_identifier_pairs.Controls.Add(pair.GroupAliasLabel);
            panel_identifier_pairs.Controls.Add(pair.GroupAliasTextBox);
            panel_identifier_pairs.Controls.Add(pair.GundiApiKeyLabel);
            panel_identifier_pairs.Controls.Add(pair.GundiApiKeyTextBox);
            panel_identifier_pairs.Controls.Add(pair.DeletePairButton);

            pair.DeletePairButton.Click += (sender, e) =>
            {
                panel_identifier_pairs.Controls.Remove(pair.GroupAliasLabel);
                panel_identifier_pairs.Controls.Remove(pair.GroupAliasTextBox);
                panel_identifier_pairs.Controls.Remove(pair.GundiApiKeyLabel);
                panel_identifier_pairs.Controls.Remove(pair.GundiApiKeyTextBox);
                panel_identifier_pairs.Controls.Remove(pair.DeletePairButton);
                identifierPairs.Remove(pair);
                render_identifier_pairs(sender, e);
            };

            render_identifier_pairs(sender, e);
        }

        private void add_group_alias(object sender, EventArgs e, GroupKeyPair ga)
        {
            // Create a new identifier pair
            var present_count = identifierPairs.Count;
            var pair = new GroupKeyPairControl(present_count);
            pair.GroupAliasTextBox.Text = ga.group_alias;
            pair.GundiApiKeyTextBox.Text = ga.api_key;

            // Group Guid is stashed here because we want it to be the value we use for filtering.
            pair.GroupGuid = ga.group_guid;

            identifierPairs.Add(pair);
            panel_identifier_pairs.Controls.Add(pair.GroupAliasLabel);
            panel_identifier_pairs.Controls.Add(pair.GroupAliasTextBox);
            panel_identifier_pairs.Controls.Add(pair.GundiApiKeyLabel);
            panel_identifier_pairs.Controls.Add(pair.GundiApiKeyTextBox);
            panel_identifier_pairs.Controls.Add(pair.DeletePairButton);

            pair.DeletePairButton.Click += (sender, e) =>
            {
                panel_identifier_pairs.Controls.Remove(pair.GroupAliasLabel);
                panel_identifier_pairs.Controls.Remove(pair.GroupAliasTextBox);
                panel_identifier_pairs.Controls.Remove(pair.GundiApiKeyLabel);
                panel_identifier_pairs.Controls.Remove(pair.GundiApiKeyTextBox);
                panel_identifier_pairs.Controls.Remove(pair.DeletePairButton);
                identifierPairs.Remove(pair);
                render_identifier_pairs(sender, e);
            };
            render_identifier_pairs(sender, e);
        }

        private void test_connection_button_Click(object sender, EventArgs e)
        {

            label_test_connection_status.Visible = false;


            if (this.database_type.SelectedItem == null)
            {
                MessageBox.Show("Please select a database type.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            IDataReader reader = null;

            if (this.database_type.SelectedItem.ToString() == "Smart Dispatch Plus")
            {
                reader = new SmartDispatchPlusV1Reader(this.database_hostname.Text,
                    this.database_name.Text, this.database_username.Text, this.database_password.Text,
                    this.database_schema.Text);

                var groups = reader.GetGroupAliases();

                groups.ForEach(group =>
                {
                    identifierPairs.ForEach(pair =>
                    {
                        if (pair.GroupGuid == group.alias)
                        {
                            MessageBox.Show("Group alias already exists.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    });
                    add_group_alias(this, EventArgs.Empty, new GroupKeyPair { group_alias = group.alias, api_key = "", group_guid = group.guid });
                });
                
            }
            if (this.database_type.SelectedItem.ToString() == "Smart One Dispatch")
            {
                reader = new SmartOneDispatchReader(this.database_hostname.Text,
                                       this.database_name.Text, this.database_username.Text, this.database_password.Text,
                                                          this.database_schema.Text);


            }
            if (this.database_type.SelectedItem.ToString() == "Kenwood KAS20")
            {
                reader = new KAS20DataReader(this.database_hostname.Text,
                                       this.database_name.Text, this.database_username.Text, this.database_password.Text);
            }

            if (this.database_type.SelectedItem.ToString() == "TRBOnet")
            {
                reader = new TrbonetPlusDataReader(this.database_hostname.Text,
                                                          this.database_name.Text, this.database_username.Text, this.database_password.Text);
            }


            if (reader != null)
            {
                var result = reader.TestConnection();

                if (result.Success)
                {
                    label_test_connection_status.Text = "Connection successful.";
                    label_test_connection_status.ForeColor = Color.Green;
                }
                else
                {
                    label_test_connection_status.Text = "Connection failed.";
                    label_test_connection_status.ForeColor = Color.DarkRed;

                    MessageBox.Show("Error: " + result.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                label_test_connection_status.Visible = true;
            }

        }

        private void database_type_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedItem = database_type.SelectedItem.ToString();

            if (selectedItem == "Smart Dispatch Plus" || selectedItem == "Smart One Dispatch")
            {
                this.database_schema.Enabled = true;
                this.database_schema_label.Enabled = true;
            }
            else
            {
                this.database_schema.Enabled = false;
                this.database_schema_label.Enabled = false;
            }
        }

        // Event handler that sets TestMessage.Visible to false
        private void AnyControl_ValueChanged(object sender, EventArgs e)
        {
            label_test_connection_status.Visible = false;
        }
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

    public class GroupKeyPairControl
    {
        public Label GroupAliasLabel { get; set; }
        public TextBox GroupAliasTextBox { get; set; }
        public Label GundiApiKeyLabel { get; set; }
        public TextBox GundiApiKeyTextBox { get; set; }
        public Button DeletePairButton { get; set; }
        public string GroupGuid { get; set; }

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

        public GroupKeyPairControl(int index)
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