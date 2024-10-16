using System;
using System.IO;
using System.Linq;
using System.Timers;
using System.Windows;
using System.Collections.Generic; // Added for HashSet
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.StrategyGenerator;

namespace NinjaTrader.Gui.NinjaScript
{
    public class AccountInfoAddon : AddOnBase
    {
        private string filePath;
        private Timer timer;
        public double index = 0;
        private readonly object fileLock = new object();

        private NTMenuItem addOnFrameworkMenuItem;
		private NTMenuItem existingMenuItemInControlCenter;

        public AccountInfoAddon()
        {
            // Initialize timer
            // MessageBox.Show("AccountInfoAddon");
            timer = new Timer(1000); // Check every minute
            timer.Elapsed += CheckAccounts;
            timer.AutoReset = true;
        }

        public class AccountObject
        {
            public string accountNumber { get; set; }
            public double initialBalance { get; set; }
            public double currentEquity { get; set; }
            public double maxEquity { get; set; }

            public AccountObject(string aNumber, double iBalance, double cEquity, double mEquity)
            {
                accountNumber = aNumber;
                initialBalance = iBalance;
                currentEquity = cEquity;
                maxEquity = mEquity;
            }
        }

        public void SetFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Invalid path provided.");
                return;
            }
            filePath = path;
            timer.Start();
            // MessageBox.Show("SetFilePath");
            CheckAccounts(null, null);
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Write account info to csv file";
				Name 		= "AccountInfoAddon";
            }
            else if (State == State.Configure)
            {
                // var window = new ConfigurationWindow(this);
                // window.ShowDialog();
            }
			else if (State == State.Terminated)
			{
				timer = null;
		        //timer?.Dispose();
			}
        }

        protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null)
				return;

			existingMenuItemInControlCenter = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem;
			if (existingMenuItemInControlCenter == null)
				return;

			addOnFrameworkMenuItem = new NTMenuItem { Header = "Export CSV", Style = Application.Current.TryFindResource("MainMenuItem") as Style };

			existingMenuItemInControlCenter.Items.Add(addOnFrameworkMenuItem);

			addOnFrameworkMenuItem.Click += OnMenuItemClick;
		}

        protected override void OnWindowDestroyed(Window window)
		{
			if (addOnFrameworkMenuItem != null && window is ControlCenter)
			{
				if (existingMenuItemInControlCenter != null && existingMenuItemInControlCenter.Items.Contains(addOnFrameworkMenuItem))
					existingMenuItemInControlCenter.Items.Remove(addOnFrameworkMenuItem);

				addOnFrameworkMenuItem.Click 	-= OnMenuItemClick;
				addOnFrameworkMenuItem 			= null;
			}
		}

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
		{
			Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => new ConfigurationWindow(this).Show()));
		}

        private void CheckAccounts(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return;

                var accounts = Cbi.Account.All.ToList();
                var existingAccounts = new HashSet<string>();

                // Lock for reading the file
                lock (fileLock)
                {
                    if (File.Exists(filePath))
                    {
                        var lines = File.ReadAllLines(filePath);
                        foreach (var line in lines.Skip(1))
                        {
                            var fields = line.Split(',');
                            if (fields.Length > 0)
                                existingAccounts.Add(fields[1]);
                        }
                    }
                }
                List<AccountObject> objList = new List<AccountObject>{};
                // Lock for writing to the file
                lock (fileLock)
                {
                    // Open StreamWriter within the lock to avoid concurrent access
                    using (StreamWriter writer = new StreamWriter(filePath, true))
                    {
                        if (new FileInfo(filePath).Length == 0)
                        {
                            writer.WriteLine("No,AccountName,AccountNumber,InitialBalance,CurrentEquity,MaxEquity");
                        }
                        foreach (Account account in accounts)
                        {
                            if (account.Name != "Backtest")
                            {
                                string accountNumber = account.Name;
                                double initialBalance = account.Get(AccountItem.CashValue, Currency.UsDollar);
                                double currentEquity = account.Get(AccountItem.CashValue, Currency.UsDollar);
                                double maxEquity = initialBalance;

                                if (!existingAccounts.Contains(accountNumber))
                                {
                                    index ++;
                                    writer.WriteLine(index + "," + accountNumber + "," + accountNumber + "," + initialBalance + "," + currentEquity + "," + maxEquity);
                                }
                                else
                                {
                                    // Call UpdateExistingAccount while still in the lock
                                    // UpdateExistingAccount(accountNumber, initialBalance, currentEquity, maxEquity);
                                    objList.Add(new AccountObject(accountNumber, initialBalance, currentEquity, maxEquity));
                                }
                            }
                        }
                    }

                }
                    foreach (var obj in objList)
                    {
                        UpdateExistingAccount(obj.accountNumber, obj.initialBalance, obj.currentEquity, obj.maxEquity);
                    }
            }
            catch (Exception ex)
            {
                Print("Error in CheckAccounts: " + ex.Message);
            }
        }

        private void UpdateExistingAccount(string accountNumber, double initialBalance, double currentEquity, double maxEquity)
        {
            lock (fileLock) // Lock for reading and writing
            {
                var lines = File.ReadAllLines(filePath).ToList();
                for (int i = 1; i < lines.Count; i++)
                {
                    var fields = lines[i].Split(',');
                    if (fields[1] == accountNumber)
                    {
                        fields[3] = currentEquity.ToString();
                        double max = 0;
                        if (double.TryParse(fields[4], out max))
                        {
                            if (currentEquity > max)
                                fields[4] = currentEquity.ToString();
                        }
                        else
                        {
                            MessageBox.Show("Parse string error: " + fields[4]);
                        }

                        lines[i] = string.Join(",", fields);
                        break;
                    }
                }
                File.WriteAllLines(filePath, lines);
            }
        }

    }

    public class ConfigurationWindow : Window
    {
        private AccountInfoAddon addon;
        private System.Windows.Controls.TextBox filePathTextBox;

        public ConfigurationWindow(AccountInfoAddon addon)
        {
            this.addon = addon;
            // MessageBox.Show("ConfigurationWindow");
            Title = "Account Info Addon Configuration";
            Width = 400; // Adjusted width of the window
            Height = 150; // Adjusted height of the window

            var mainPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Margin = new Thickness(10) // Add margin for padding
            };

            var inputPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal, // Horizontal layout for label and textbox
                Margin = new Thickness(0, 0, 0, 10) // Margin below the input panel
            };

            var label = new System.Windows.Controls.Label
            {
                Content = "CSV File Path:",
                VerticalAlignment = VerticalAlignment.Center // Center the label vertically
            };

            filePathTextBox = new System.Windows.Controls.TextBox
            {
                Width = 250, // Set width of the textbox
                Height = 25, // Set height of the textbox
                Margin = new Thickness(5, 0, 0, 0), // Margin to the left of the textbox
                Text = @"D:\account_info.csv", // Set default value in the textbox
            };

            inputPanel.Children.Add(label);
            inputPanel.Children.Add(filePathTextBox);

            var button = new System.Windows.Controls.Button
            {
                Content = "Save",
                Width = 100, // Set width of the button
                Height = 30, // Set height of the button
                Margin = new Thickness(0, 10, 0, 0) // Margin above the button
            };

            button.Click += SaveButton_Click;

            mainPanel.Children.Add(inputPanel);
            mainPanel.Children.Add(button);
            Content = mainPanel;

        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var path = filePathTextBox.Text;
            if (!string.IsNullOrEmpty(path))
            {
                // MessageBox.Show(path);
                this.addon.SetFilePath(path);
                Close();
            }
            else
            {
                MessageBox.Show("Please enter a valid file path.");
            }
        }
    }
}
