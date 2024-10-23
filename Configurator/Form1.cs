using System.Text.Json;
using DataPump;

namespace Configurator
{
    public partial class service_config_form : Form
    {
        public service_config_form()
        {
            identifierPairs = new List<PairTextBox>();
            InitializeComponent();
            LoadFromJson();
        }

        private void button_save_click(object sender, EventArgs e)
        {

            var groupKeyPairs = new List<GroupKeyPair>();
            this.identifierPairs.ForEach(pair =>
            {
                var gp = new GroupKeyPair {
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
            var pair = new PairTextBox(present_count);
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

        private void add_group_alias(object sender, EventArgs e, GroupAlias ga)
        {
            // Create a new identifier pair
            var present_count = identifierPairs.Count;
            var pair = new PairTextBox(present_count);
            pair.GroupAliasTextBox.Text = ga.alias;
            pair.GundiApiKeyTextBox.Text = ga.guid;
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

            test_connection_status_lablel.Visible = false;


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
                Console.WriteLine(JsonSerializer.Serialize(groups));

                groups.ForEach(ga => add_group_alias(sender, e, ga));
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
                    test_connection_status_lablel.Text = "Connection successful.";
                    test_connection_status_lablel.ForeColor = Color.Green;
                }
                else
                {
                    test_connection_status_lablel.Text = "Connection failed.";
                    test_connection_status_lablel.ForeColor = Color.DarkRed;

                    MessageBox.Show("Error: " + result.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                test_connection_status_lablel.Visible = true;
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
            test_connection_status_lablel.Visible = false;
        }
    }


}