using System.Text.Json;
using DataPump;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using System.Configuration;
using worker;
using service;


namespace Configurator
{

    public partial class RadioServiceConfigForm : Form
    {

        private RouteConfiguration configuration;
        private List<GundiConnectionControl> gundiConnectionControls;
        private BindingList<GroupAlias> groupAliases;

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
        private Button test_connection_button;
        private Label label_test_connection_status;
        private Label database_name_label;
        private TextBox database_name;
        private Label database_schema_label;
        private TextBox database_schema;
        private Label save_message_label;

        private AppSettingsManager appSettingsManager;

        public RadioServiceConfigForm()
        {
            groupAliases = new BindingList<GroupAlias>();
            gundiConnectionControls = new List<GundiConnectionControl>();
            InitializeComponent();

            // Load a saved configuration.
            appSettingsManager = new AppSettingsManager("appsettings.json");
            //configuration = loadConfiguration();
            var settings = appSettingsManager.LoadValue();

            configuration = settings.RouteConfiguration;

            BindControls();
            InitializeGundiConnectionControls(configuration);

            if (configuration.DatabaseType == null)
            {
                configuration.DatabaseType = database_type.SelectedItem as SupportedReader;
            }

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
            database_type.DataSource = new BindingList<SupportedReader>(RadioDataPumpService.supportedReaders);
            database_type.DataBindings.Add("SelectedItem", configuration, "DatabaseType", false, DataSourceUpdateMode.OnPropertyChanged);
            database_hostname.DataBindings.Add("Text", configuration, "Hostname", false, DataSourceUpdateMode.OnPropertyChanged);
            database_name.DataBindings.Add("Text", configuration, "DatabaseName", false, DataSourceUpdateMode.OnPropertyChanged);
            database_username.DataBindings.Add("Text", configuration, "Username", false, DataSourceUpdateMode.OnPropertyChanged);
            database_schema.DataBindings.Add("Text", configuration, "DatabaseSchema", false, DataSourceUpdateMode.OnPropertyChanged);
            database_password.DataBindings.Add("Text", configuration, "Password", false, DataSourceUpdateMode.OnPropertyChanged);
        }

        private void saveConfiguration()
        {

            configuration.gundiConnections.ForEach(gc =>
            {
                gc.ApiKey = gc.ApiKey?.Trim() ?? "";
                gc.Destination = gc.Destination?.Trim() ?? "";
                gc.ConnectionName = gc.ConnectionName?.Trim() ?? "";
            });
            appSettingsManager.SaveValue();


        }

        private void SaveButtonClick(object sender, EventArgs e)
        {

            saveConfiguration();
            setStatusMessage("Configuration Saved", Color.Green);

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
                card.TopOffset = 10 + index * 190;
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

            //gundiConnectionCard.GroupsListBox.DataSource = groupAliases;
            gundiConnectionCard.GroupsListBox.DisplayMember = "alias";
            //gundiConnectionCard.GroupsListBox.ValueMember = "guid";
            gundiConnectionCard.GroupsListBox.CheckOnClick = true;


            // Add configured items to the list and listbox.
            foreach (var item in gundiConnection.GroupAliases)
            {
                if (!groupAliases.Contains(item))
                {
                    groupAliases.Add(item);
                }

            }

            // Ensure the listbox control contains all group items.
            foreach (var item in groupAliases)
            {
                if (!gundiConnectionCard.GroupsListBox.Items.Contains(item))
                {
                    gundiConnectionCard.GroupsListBox.Items.Add(item, true);
                }

            }

            groupAliases.ListChanged += (s, args) =>
            {
                foreach (var item in groupAliases)
                {
                    if (!gundiConnectionCard.GroupsListBox.Items.Contains(item))
                    {
                        gundiConnectionCard.GroupsListBox.Items.Add(item);
                    }

                }
            };

            // This ensures the DataSource has items equivalent to what is being read from a config file.
            if (gundiConnection.GroupAliases != null)
            {

                for (int i = 0; i < gundiConnectionCard.GroupsListBox.Items.Count; i++)
                {
                    if (gundiConnection.GroupAliases.Contains(gundiConnectionCard.GroupsListBox.Items[i]))
                    {
                        gundiConnectionCard.GroupsListBox.SetItemChecked(i, true);
                    }
                    else
                    {
                        gundiConnectionCard.GroupsListBox.SetItemChecked(i, false);
                    }
                }
            }


            // Suppress highlighting of the selected item
            gundiConnectionCard.GroupsListBox.SelectedIndexChanged += (s, args) =>
            {
                gundiConnection.GroupAliases = gundiConnectionCard.GroupsListBox.CheckedItems.Cast<GroupAlias>().ToList();
                gundiConnectionCard.GroupsListBox.ClearSelected();
            };

            // Suppress highlighting of the selected item
            gundiConnectionCard.GroupsListBox.MouseDown += (s, args) =>
            {
                // Find the item under the mouse pointer
                int index = gundiConnectionCard.GroupsListBox.IndexFromPoint(args.Location);

                if (index != -1)
                {
                    // Toggle the checked state without changing the selection
                    gundiConnectionCard.GroupsListBox.SetItemChecked(index, !gundiConnectionCard.GroupsListBox.GetItemChecked(index));

                    // Prevent item selection
                    gundiConnectionCard.GroupsListBox.ClearSelected();
                }
            };

            var selectedReader = (SupportedReader)database_type.SelectedItem;

            if (selectedReader.Type == RadioServiceConfiguration.ReaderType.SmartDispatchPlus
                               || selectedReader.Type == RadioServiceConfiguration.ReaderType.SmartOneDispatch)
            {
                gundiConnectionCard.GroupsLabel.ForeColor = Color.Black;
                gundiConnectionCard.GroupsListBox.Enabled = true;
            }
            else
            {
                gundiConnectionCard.GroupsLabel.ForeColor = Color.Gray;
                gundiConnectionCard.GroupsListBox.Enabled = false;
            }

            database_type.SelectedIndexChanged += (s, args) =>
            {
                var selectedReader = (SupportedReader)database_type.SelectedItem;

                if (selectedReader.Type == RadioServiceConfiguration.ReaderType.SmartDispatchPlus
                                   || selectedReader.Type == RadioServiceConfiguration.ReaderType.SmartOneDispatch)
                {
                    gundiConnectionCard.GroupsLabel.ForeColor = Color.Black;
                    gundiConnectionCard.GroupsListBox.Enabled = true;
                }
                else
                {
                    gundiConnectionCard.GroupsLabel.ForeColor = Color.Gray;
                    gundiConnectionCard.GroupsListBox.Enabled = false;
                }

                configuration.gundiConnections.ForEach(route =>
                {
                    route.GroupAliases.Clear();
                });
                gundiConnectionCard.GroupsListBox.Items.Clear();
            };


            tabGundiConnection.Controls.Add(gundiConnectionCard.Panel);

            gundiConnectionCard.DeleteButton.Click += (sender, e) =>
            {

                configuration.gundiConnections.Remove(gundiConnection);
                tabGundiConnection.Controls.Remove(gundiConnectionCard.Panel);
                gundiConnectionControls.Remove(gundiConnectionCard);
                adjustConnectionPositions(sender, e);
            };
            adjustConnectionPositions(sender, e);
        }

        private void Database_type_SelectedIndexChanged(object? sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void testConnectionButtonClick(object sender, EventArgs e)
        {

            label_test_connection_status.Visible = false;


            if (this.database_type.SelectedItem == null)
            {
                MessageBox.Show("Please select a database type.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            IDataReader reader = null;

            var selectedReader = (SupportedReader)database_type.SelectedItem;


            if (selectedReader.Type == RadioServiceConfiguration.ReaderType.SmartDispatchPlus)
            {
                reader = new SmartDispatchPlusV1Reader(this.database_hostname.Text,
                    this.database_name.Text, this.database_username.Text, this.database_password.Text,
                    this.database_schema.Text);

                reader.GetGroupAliases().ForEach(item =>
                {
                    if (!groupAliases.Contains(item))
                    {
                        groupAliases.Add(item);
                    }
                });

            }
            if (selectedReader.Type == RadioServiceConfiguration.ReaderType.SmartOneDispatch)
            {
                reader = new SmartOneDispatchReader(this.database_hostname.Text,
                                       this.database_name.Text, this.database_username.Text, this.database_password.Text,
                                                          this.database_schema.Text);


            }
            if (selectedReader.Type == RadioServiceConfiguration.ReaderType.KAS20)
            {
                reader = new KAS20DataReader(this.database_hostname.Text,
                                       this.database_name.Text, this.database_username.Text, this.database_password.Text);
            }

            if (selectedReader.Type == RadioServiceConfiguration.ReaderType.TrbonetPlus)
            {
                reader = new TrbonetPlusDataReader(this.database_hostname.Text,
                                                          this.database_name.Text, this.database_username.Text, this.database_password.Text);
            }


            if (reader != null)
            {
                var result = reader.TestConnection();

                if (result.Success)
                {
                    setStatusMessage("Connection successful.", Color.Green);
                }
                else
                {
                    setStatusMessage("Connection failed.", Color.DarkRed);
                    MessageBox.Show("Error: " + result.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                label_test_connection_status.Visible = true;


            }

        }
        private Timer _timer = new Timer();
        private void setStatusMessage(string message, Color color)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = color;
            if (_timer.Enabled)
            {
                _timer.Stop();
            }
            _timer.Interval = 5000; // 2 seconds
            _timer.Tick += (s, args) =>
            {
                _timer.Stop();
                statusLabel.Text = "";

            };
            _timer.Start();

        }

        private void database_type_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedItem = (SupportedReader)database_type.SelectedItem;

            if (selectedItem.Type == RadioServiceConfiguration.ReaderType.SmartDispatchPlus
                || selectedItem.Type == RadioServiceConfiguration.ReaderType.SmartOneDispatch)
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

        private void fetchGroupsButton_Click(object sender, EventArgs e)
        {
            testConnectionButtonClick(sender, e);
        }

    }

    public class GundiConnectionControl
    {
        public Panel Panel { get; set; }

        public Label ConnectionNameLabel { get; set; }
        public TextBox ConnectionNameTextBox { get; set; }

        public Label GundiApiKeyLabel { get; set; }
        public TextBox GundiApiKeyTextBox { get; set; }

        public Label GundiDestinationLabel { get; set; }
        public ComboBox GundiDestinationComboBox { get; set; }

        public Label GroupsLabel { get; set; }
        public CheckedListBox GroupsListBox { get; set; }

        public CheckBox SendEverythingCheckBox { get; set; }

        public Button DeleteButton { get; set; }


        private int top_offset;
        public int TopOffset
        {
            get { return top_offset; }

            set
            {
                top_offset = value;
                Panel.Top = top_offset;
            }
        }

        public GundiConnectionControl(int index)
        {
            Panel = new Panel()
            {
                Width = 680,
                Height = 180,
            };
            Panel.BorderStyle = BorderStyle.FixedSingle;

            // Initialize the source identifier label
            ConnectionNameLabel = new Label()
            {
                Text = "Connection Name",
                Top = 5,
                Left = 10,
                Width = 200,
            };

            // Initialize the source identifier textbox
            ConnectionNameTextBox = new TextBox()
            {
                Top = 5,
                Left = 220,
                Width = 230,
                TabIndex = 1
            };

            // Initialize the destination identifier label
            GundiDestinationLabel = new Label()
            {
                Text = "Gundi Service URL",
                Top = 35,
                Left = 10,
                Width = 200
            };

            // Initialize the destination identifier textbox
            GundiDestinationComboBox = new ComboBox()
            {
                Top = 35,
                Left = 220,
                Width = 230,
                TabIndex = 2
            };

            //GundiDestinationComboBox.Items.AddRange(new object[] { "https://sensors.api.gundiservice.org", "https://sensors.api.stage.gundiservice.org" });
            GundiDestinationComboBox.Items.AddRange(new object[] { "https://sensors.api.gundiservice.org" });

            GundiDestinationComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

            // Initialize the destination identifier label
            GundiApiKeyLabel = new Label()
            {
                Text = "Gundi API Key",
                Top = 65,
                Left = 10,
                Width = 200
            };

            // Initialize the destination identifier textbox
            GundiApiKeyTextBox = new TextBox()
            {
                Top = 65,
                Left = 220,
                Width = 230,
                TabIndex = 3
            };

            SendEverythingCheckBox = new CheckBox()
            {
                Text = "Send Everything",
                Top = 95,
                Left = 10,
                Width = 220,
                TabIndex = 4
            };

            GroupsLabel = new Label()
            {
                Text = "Groups",
                Top = 5,
                Left = 460,
                Width = 150,
                TabIndex = 5,
            };

            GroupsListBox = new CheckedListBox()
            {
                Top = 35,
                Left = 460,
                Width = 150,
                Height = 100,
                TabIndex = 6,
            };
            DeleteButton = new Button()
            {
                Text = "Remove",
                Top = 5,
                Left = 610,
                Width = 60,
                TabIndex = 5
            };

            var controls = new List<Control>() { ConnectionNameLabel, ConnectionNameTextBox, GundiApiKeyLabel, GundiApiKeyTextBox, GundiDestinationLabel, GundiDestinationComboBox,
                SendEverythingCheckBox, GroupsLabel, GroupsListBox, DeleteButton };
            controls.ForEach(Panel.Controls.Add);

            DeleteButton.Click += (sender, e) =>
            {
                Panel.Dispose();
                //ConnectionNameLabel.Dispose();
                //ConnectionNameTextBox.Dispose();
                //GundiApiKeyLabel.Dispose();
                //GundiApiKeyTextBox.Dispose();
                //GundiDestinationComboBox.Dispose();
                //GundiDestinationLabel.Dispose();
                //SendEverythingCheckBox.Dispose();
                //DeleteButton.Dispose();
            };


        }
    }
}