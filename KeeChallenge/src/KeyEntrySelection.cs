using System;
using System.Drawing;
using System.Windows.Forms;

using KeePass.UI;
using System.Text;

namespace KeeChallenge
{
    public partial class KeyEntrySelection : Form
    {
        private byte[] m_response;

        public byte[] Response
        {
            get { return m_response; }
            private set { m_response = value; }
        }

        public KeyEntrySelection(KeeChallengeProv parent)
        {
            InitializeComponent();

            Icon = Icon.FromHandle(Properties.Resources.yubikey.GetHicon());
        }


        public void OnClosing(object o, FormClosingEventArgs e)
        {
            GlobalWindowManager.RemoveWindow(this);
        }
    }
}
