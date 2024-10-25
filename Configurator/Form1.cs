using System.Text.Json;
using DataPump;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using System.Configuration;

namespace Configurator
{
    public partial class RadioServiceConfigForm : Form
    {

        private RouteConfiguration configuration;
        private List<GundiConnectionControl> gundiConnectionControls;
        private List<GroupAlias> groupAliases = new List<GroupAlias>();

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
        private Button button_add_id_pair;
        private Label group_id_map_label;
        private Button test_connection_button;
        private Label label_test_connection_status;
        private Label database_name_label;
        private TextBox database_name;
        private Label database_schema_label;
        private TextBox database_schema;
        private Label save_message_label;


        public RadioServiceConfigForm()
        {
            gundiConnectionControls = new List<GundiConnectionControl>();
            InitializeComponent();
            configuration = LoadFromJson();
            InitializeGundiConnectionControls(configuration);
            BindControls();
        }

        private void InitializeGundiConnectionControls(RouteConfiguration configuration)
        {
            configuration.gundiConnections.ForEach(route =>
               addGundiConnection(null, null, route)
             );
        }
        private void BindControls()
        {
            // Bind the TextBox controls to the GundiConnection properties
            database_hostname.DataBindings.Add("Text", configuration, "Hostname", false, DataSourceUpdateMode.OnPropertyChanged);
            database_name.DataBindings.Add("Text", configuration, "DatabaseName", false, DataSourceUpdateMode.OnPropertyChanged);
            database_type.DataBindings.Add("Text", configuration, "DatabaseType", false, DataSourceUpdateMode.OnPropertyChanged);
            database_username.DataBindings.Add("Text", configuration, "Username", false, DataSourceUpdateMode.OnPropertyChanged);
            database_schema.DataBindings.Add("Text", configuration, "DatabaseSchema", false, DataSourceUpdateMode.OnPropertyChanged);
            database_password.DataBindings.Add("Text", configuration, "Password", false, DataSourceUpdateMode.OnPropertyChanged);
        }

        private void saveConfiguration()
        {

            // Serialize to JSON
            string json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });

            // Write to a file
            string filePath = "config.json";
            File.WriteAllText(filePath, json);

            MessageBox.Show("Configuration saved to " + filePath, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private RouteConfiguration LoadFromJson()
        {
            string filePath = "config.json";

            configuration = new RouteConfiguration();
            // Check if the file exists
            if (File.Exists(filePath))
            {
                try
                {
                    // Read from the JSON file
                    string json = File.ReadAllText(filePath);

                    // Deserialize the JSON
                    configuration = JsonSerializer.Deserialize<RouteConfiguration>(json);

                    Console.WriteLine("Loaded: " + configuration);
                    //// Set the selected item for the ComboBox
                    //if (!string.IsNullOrEmpty(configuration?.DatabaseType) && database_type.Items.Contains(configuration.DatabaseType))
                    //{
                    //    database_type.SelectedItem = configuration.DatabaseType;
                    //}

                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading configuration: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return configuration;
        }


        private void button_save_click(object sender, EventArgs e)
        {

            saveConfiguration();

        }

        private void button_close_click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void adjustConnectionPositions(object sender, EventArgs e)
        {
            var index = 0;
            gundiConnectionControls.ForEach(card =>
            {
                card.TopOffset = 5 + index * 125;
                index = index + 1;
            });
        }


        private void addGundiConnection(object sender, EventArgs e)
        {
            var gundiConnection = new GundiConnection();
            gundiConnection.ConnectionName = "Connection " + (configuration.gundiConnections.Count + 1);
            gundiConnection.Destination = "https://sensors.api.gundiservice.org";   
            configuration.gundiConnections.Add(gundiConnection);
            addGundiConnection(sender, e, gundiConnection);
        }
        private void addGundiConnection(object sender, EventArgs e, GundiConnection gundiConnection)
        {

            var gundiConnectionCard = new GundiConnectionControl(configuration.gundiConnections.Count);
            gundiConnectionControls.Add(gundiConnectionCard);

            gundiConnectionCard.ConnectionNameTextBox.DataBindings.Add("Text", gundiConnection, "ConnectionName", false, DataSourceUpdateMode.OnPropertyChanged);
            gundiConnectionCard.GundiApiKeyTextBox.DataBindings.Add("Text", gundiConnection, "ApiKey", false, DataSourceUpdateMode.OnPropertyChanged);
            gundiConnectionCard.GundiDestinationComboBox.DataBindings.Add("Text", gundiConnection, "Destination", false, DataSourceUpdateMode.OnPropertyChanged);
            gundiConnectionCard.SendEverythingCheckBox.DataBindings.Add("Checked", gundiConnection, "SendEverything", false, DataSourceUpdateMode.OnPropertyChanged);

            gundiConnectionCard.GroupsListBox.DataSource = groupAliases;
            gundiConnectionCard.GroupsListBox.DisplayMember = "alias";
            gundiConnectionCard.GroupsListBox.ValueMember = "guid";
            gundiConnectionCard.GroupsListBox.SelectionMode = SelectionMode.MultiSimple;

            if (gundiConnection.GroupAliases != null)
            {
                gundiConnection.GroupAliases.ForEach(thing =>
                {
                    groupAliases.Add(thing);
                    gundiConnectionCard.GroupsListBox.SelectedItems.Add(thing);
                });
            }

            gundiConnectionCard.GroupsListBox.SelectedIndexChanged += (s, args) =>
            {
                gundiConnection.GroupAliases = gundiConnectionCard.GroupsListBox.SelectedItems.Cast<GroupAlias>().ToList();
            };

            //gundiConnectionCard.GroupsListBox.DataBindings.Add("SelectedItems", gundiConnection, "GroupAliases", false, DataSourceUpdateMode.OnPropertyChanged);
            
            tabGundiConnection.Controls.Add(gundiConnectionCard.ConnectionNameLabel);
            tabGundiConnection.Controls.Add(gundiConnectionCard.ConnectionNameTextBox);
            tabGundiConnection.Controls.Add(gundiConnectionCard.GundiApiKeyLabel);
            tabGundiConnection.Controls.Add(gundiConnectionCard.GundiApiKeyTextBox);
            tabGundiConnection.Controls.Add(gundiConnectionCard.GundiDestinationLabel);
            tabGundiConnection.Controls.Add(gundiConnectionCard.GundiDestinationComboBox);
            tabGundiConnection.Controls.Add(gundiConnectionCard.SendEverythingCheckBox);
            tabGundiConnection.Controls.Add(gundiConnectionCard.DeleteButton);
            tabGundiConnection.Controls.Add(gundiConnectionCard.GroupsLabel);
            tabGundiConnection.Controls.Add(gundiConnectionCard.GroupsListBox);
            gundiConnectionCard.DeleteButton.Click += (sender, e) =>
            {
                tabGundiConnection.Controls.Remove(gundiConnectionCard.ConnectionNameLabel);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.ConnectionNameTextBox);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.GundiApiKeyLabel);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.GundiApiKeyTextBox);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.GundiDestinationLabel);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.GundiDestinationComboBox);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.SendEverythingCheckBox);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.GroupsLabel);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.GroupsListBox);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.DeleteButton);

                configuration.gundiConnections.Remove(gundiConnection);
                gundiConnectionControls.Remove(gundiConnectionCard);
                adjustConnectionPositions(sender, e);
            };
            adjustConnectionPositions(sender, e);
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

                groupAliases = reader.GetGroupAliases();

                //groups.ForEach(group =>
                //{
                //    identifierPairs.ForEach(pair =>
                //    {
                //        if (pair.GroupGuid == group.alias)
                //        {
                //            MessageBox.Show("Group alias already exists.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                //            return;
                //        }
                //    });
                //    addGundiConnection(this, EventArgs.Empty, new GroupKeyPair { group_alias = group.alias, api_key = "", group_guid = group.guid });
                //    groupsListBox.Items.Add(group);
                //});

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
                    statusLabel.Text = "Connection successful.";
                    statusLabel.ForeColor = Color.Green;
                }
                else
                {
                    label_test_connection_status.Text = "Connection failed.";
                    label_test_connection_status.ForeColor = Color.DarkRed;
                    statusLabel.Text = "Connection failed.";
                    statusLabel.ForeColor = Color.DarkRed;
                    MessageBox.Show("Error: " + result.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                label_test_connection_status.Visible = true;


                Timer timer = new Timer();
                timer.Interval = 5000; // 2 seconds
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    statusLabel.Text = "";

                };
                timer.Start();
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
            statusLabel.Text = "";
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void tabDatabaseConnection_Click(object sender, EventArgs e)
        {

        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void toolStripStatusLabel1_Click(object sender, EventArgs e)
        {

        }

        private void gundiConnectionsAddButtonClick(object sender, EventArgs e)
        {
            addGundiConnection(sender, e);
        }
    }

    public class RouteConfiguration
    {
        public string Hostname { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string DatabaseName { get; set; }
        public string DatabaseType { get; set; }
        public string DatabaseSchema { get; set; }

        public List<GundiConnection> gundiConnections { get; set; }

        public RouteConfiguration()
        {
            gundiConnections = new List<GundiConnection>();
        }
    }

    public class GundiConnectionControl
    {
        public Label ConnectionNameLabel { get; set; }
        public TextBox ConnectionNameTextBox { get; set; }

        public Label GundiApiKeyLabel { get; set; }
        public TextBox GundiApiKeyTextBox { get; set; }

        public Label GundiDestinationLabel { get; set; }
        public ComboBox GundiDestinationComboBox { get; set; }

        public Label GroupsLabel { get; set; }
        public ListBox GroupsListBox { get; set; }

        public CheckBox SendEverythingCheckBox { get; set; }

        public Button DeleteButton { get; set; }


        private int top_offset;
        public int TopOffset
        {
            get { return top_offset; }

            set
            {
                top_offset = value;
                ConnectionNameLabel.Top = top_offset;
                ConnectionNameTextBox.Top = top_offset;
                ConnectionNameTextBox.TabIndex = top_offset + 500;
                GundiDestinationLabel.Top = top_offset + 30;
                GundiDestinationComboBox.Top = top_offset + 30;
                GundiDestinationComboBox.TabIndex = top_offset + 501;
                GundiApiKeyLabel.Top = top_offset + 60;
                GundiApiKeyTextBox.Top = top_offset + 60;
                GundiApiKeyTextBox.TabIndex = top_offset + 502;
                SendEverythingCheckBox.Top = top_offset + 90;
                SendEverythingCheckBox.TabIndex = top_offset + 503;

                GroupsLabel.Top = top_offset;
                GroupsListBox.Top = top_offset + 30;
                DeleteButton.Top = top_offset;
            }
        }

        public GundiConnectionControl(int index)
        {
            // Initialize the source identifier label
            ConnectionNameLabel = new Label()
            {
                Text = "Connection Name",
                Top = 0,
                Left = 10,
                Width = 200,
            };

            // Initialize the source identifier textbox
            ConnectionNameTextBox = new TextBox()
            {
                Top = 0,
                Left = 220,
                Width = 200,
                TabIndex = 1
            };

            // Initialize the destination identifier label
            GundiDestinationLabel = new Label()
            {
                Text = "Gundi Service URL",
                Top = 0,
                Left = 10,
                Width = 200
            };

            // Initialize the destination identifier textbox
            GundiDestinationComboBox = new ComboBox()
            {
                Top = 0,
                Left = 220,
                Width = 200,
                TabIndex = 2
            };

            GundiDestinationComboBox.Items.AddRange(new object[] { "https://sensors.api.gundiservice.org", "https://sensors.api.stage.gundiservice.org" });
            GundiDestinationComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;

            // Initialize the destination identifier label
            GundiApiKeyLabel = new Label()
            {
                Text = "Gundi API Key",
                Top = 0,
                Left = 10,
                Width = 200
            };

            // Initialize the destination identifier textbox
            GundiApiKeyTextBox = new TextBox()
            {
                Top = 0,
                Left = 220,
                Width = 200,
                TabIndex = 3
            };

            SendEverythingCheckBox = new CheckBox()
            {
                Text = "Send Everything",
                Top = 0,
                Left = 10,
                Width = 250,
                TabIndex = 4
            };

            GroupsLabel = new Label()
            {
                Text = "Groups",
                Top = 0,
                Left = 430,
                Width = 150,
                TabIndex = 5,
            };

            GroupsListBox = new ListBox()
            {
                Top = 0,
                Left = 430,
                Width = 150,
                Height = 100,
                TabIndex = 6
            };
            DeleteButton = new Button()
            {
                Text = "Remove",
                Top = 0,
                Left = 580,
                Width = 90,
                TabIndex = 5
            };

            DeleteButton.Click += (sender, e) =>
            {
                ConnectionNameLabel.Dispose();
                ConnectionNameTextBox.Dispose();
                GundiApiKeyLabel.Dispose();
                GundiApiKeyTextBox.Dispose();
                GundiDestinationComboBox.Dispose();
                GundiDestinationLabel.Dispose();
                SendEverythingCheckBox.Dispose();
                DeleteButton.Dispose();
            };
        }
    }
}