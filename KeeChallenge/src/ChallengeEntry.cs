using System;
using System.Drawing;
using System.Windows.Forms;

using KeePass.UI;
using System.Text;

namespace KeeChallenge
{
    public partial class ChallengeEntry : Form
    {
        private byte[] m_response;

        public byte[] Response
        {
            get { return m_response; }
            private set { m_response = value; }
        }

        public ChallengeEntry(KeeChallengeProv parent)
        {
            InitializeComponent();

            Icon = Icon.FromHandle(Properties.Resources.yubikey.GetHicon());
        }


        public void OnClosing(object o, FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                if (secretTextBox.Text.Length == 0 || secretTextBox.Text.Length > 256)
                {
                    //invalid key
                    string outMessage = string.Format("Error: challenge cannot be longer than {0:C} characters", 256);
                    MessageBox.Show(outMessage);
                    return;
                }
            }
            GlobalWindowManager.RemoveWindow(this);
        }

        private void OKButton_Click(object sender, EventArgs e)
        {
            m_response = new byte[256];
            secretTextBox.Text = secretTextBox.Text.Replace(" ", string.Empty); //remove spaces

            if (secretTextBox.Text.Length > 0 && secretTextBox.Text.Length <=256)
            {
                int i = 0;

                byte[] bytes = Encoding.ASCII.GetBytes(secretTextBox.Text);
                bytes.CopyTo(m_response, 0);

                //0 pad the remaing parts of the challenge
                for (i = secretTextBox.Text.Length; i < 256; i ++)
                {
                    m_response[i ] = 0;
                }
            }
            
        }
    }
}
