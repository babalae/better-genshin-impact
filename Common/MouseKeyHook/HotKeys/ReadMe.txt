Until a separate, full-featured test version is ready, here's a quick update that can be made to the TestFormHookListeners:

		//HotKeySetsListener inherits KeyboardHookListener
		private readonly HotKeySetsListener m_KeyboardHookManager;
        private readonly MouseHookListener m_MouseHookManager;

        public TestFormHookListeners()
        {
            InitializeComponent();
            //m_KeyboardHookManager = new KeyboardHookListener(new GlobalHooker());
            //m_KeyboardHookManager.Enabled = true;

            m_MouseHookManager = new MouseHookListener( new GlobalHooker() ) { Enabled = true };

            HotKeySetCollection hkscoll = new HotKeySetCollection();
            m_KeyboardHookManager = new HotKeySetsListener( hkscoll, new GlobalHooker() ) { Enabled = true };

            BuildHotKeyTests( hkscoll );
        }

        private void BuildHotKeyTests( HotKeySetCollection hkscoll )
        {
			//Hot Keys are enabled by default.  Use the Enabled property to adjust.
            hkscoll.Add( BindHotKeySet( new[] { Keys.T, Keys.LShiftKey }, null, OnHotKeyDownOnce1, OnHotKeyDownHold1, OnHotKeyUp1, "test1" ) );
            hkscoll.Add( BindHotKeySet( new[] { Keys.T, Keys.LControlKey, Keys.RControlKey }, new[] { Keys.LControlKey, Keys.RControlKey }, OnHotKeyDownGeneral2, OnHotKeyDownGeneral2, OnHotKeyUp1, "test2" ) );
        }

        private static HotKeySet BindHotKeySet( IEnumerable<Keys> ks,
                                                IEnumerable<Keys> xorKeys,
                                                HotKeySet.HotKeyHandler onEventDownOnce,
                                                HotKeySet.HotKeyHandler onEventDownHold,
                                                HotKeySet.HotKeyHandler onEventUp,
                                                string name )
        {

			//Declare ALL Keys that will be available in this set, including any keys you want to register as an either/or subset
            HotKeySet hks = new HotKeySet( ks );

			//Indicates that the keys in this array will be treated as an OR rather than AND: LShiftKey or RShiftKey
			//The keys MUST be a subset of the ks Keys array.  
			if ( hks.RegisterExclusiveOrKey( xorKeys ) == Keys.None )	//Keys.None indicates an error
            {
                MessageBox.Show( null, @"Unable to register subset: " + String.Join( ", ", xorKeys ),
                                 @"Subset registration error", MessageBoxButtons.OK, MessageBoxIcon.Error );
            }

            hks.OnHotKeysDownOnce += onEventDownOnce;	//The first time the key is down
            hks.OnHotKeysDownHold += onEventDownHold;	//Fired as long as the user holds the hot keys down but is not fired the first time.
            hks.OnHotKeysUp += onEventUp;				//Whenever a key from the set is no longer being held down

            hks.Name = ( name ?? String.Empty );

            return hks;

        }

        private void GeneralHotKeyEvent( object sender, DateTime timeTriggered, string eventType )
        {
            HotKeySet hks = sender as HotKeySet;
            string kstring = String.Join( ", ", hks.HotKeys );
            Log( String.Format( "{0}: {2} {1} - {3}\r\n", timeTriggered.TimeOfDay, eventType, hks.Name, kstring ) );
        }

        private void OnHotKeyDownGeneral2( object sender, HotKeyArgs e )
        {
            GeneralHotKeyEvent( sender, e.Time, "ONCE/HOLD" );
        }

        private void OnHotKeyDownOnce1( object sender, HotKeyArgs e )
        {
            GeneralHotKeyEvent( sender, e.Time, "ONCE" );
        }

        private void OnHotKeyDownHold1( object sender, HotKeyArgs e )
        {
            GeneralHotKeyEvent( sender, e.Time, "HOLD" );
        }

        private void OnHotKeyUp1( object sender, HotKeyArgs e )
        {
            GeneralHotKeyEvent( sender, e.Time, "UP" );
        }



