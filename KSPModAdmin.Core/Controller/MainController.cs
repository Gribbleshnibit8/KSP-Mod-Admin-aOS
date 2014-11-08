﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KSPModAdmin.Core.Config;
using KSPModAdmin.Core.Model;
using KSPModAdmin.Core.Utils;
using KSPModAdmin.Core.Utils.Localization;
using KSPModAdmin.Core.Utils.Logging;
using KSPModAdmin.Core.Views;

namespace KSPModAdmin.Core.Controller
{
    public class MainController : IMessageReceiver
    {
        private const string KSPMA_LOG_FILENAME = "KSPMA.log";

        #region Properties

        /// <summary>
        /// Gets the singleton of this class.
        /// </summary>
        protected static MainController Instance { get { return mInstance ?? (mInstance = new MainController()); } }
        protected static MainController mInstance = null;

        /// <summary>
        /// Flag to determine if the shut down process is running.
        /// </summary>
        public static bool IsShutDown { get; set; }

        /// <summary>
        /// Gets or sets the view of the controller.
        /// </summary>
        public static frmMain View { get; protected set; }

        /// <summary>
        /// Gets or sets the selected KSP path.
        /// </summary>
        public static List<NoteNode> KnownKSPPaths
        {
            get { return (View == null) ? new List<NoteNode>() : View.KnownKSPPaths; } 
            set { if (View != null) View.KnownKSPPaths = value; }
        }

        /// <summary>
        /// Gets or sets the selected KSP path.
        /// </summary>
        public static string SelectedKSPPath
        {
            get { return (View == null) ? string.Empty : View.SelectedKSPPath; }
            set
            {
                if (View != null)
                {
                    View.SilentSetSelectedKSPPath(value);
                    OptionsController.SelectedKSPPath = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected KSP path silently (without throwing events).
        /// </summary>
        internal static string _SelectedKSPPath
        {
            get { return SelectedKSPPath; }
            set
            {
                if (View != null)
                    View.SilentSetSelectedKSPPath(value);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Private constructor (use static function only).
        /// </summary>
        private MainController()
        {
        }

        /// <summary>
        /// Static constructor. Creates a singleton of this class.
        /// </summary>
        static MainController()
        {
            if (mInstance == null)
                mInstance = new MainController();
        }

        #endregion

        public static void ShowMainForm()
        {
            try
            {
                SetupLogFile();

                LoadLanguages();

                View = new frmMain();

                Initialize();

                if (!IsShutDown)
                    Application.Run(View);
            }
            catch (Exception ex)
            {
                string msg = string.Format("Unexpected runtime error: \"{0}\"", ex.Message);
                string displayMsg = string.Format("{0}{1}{1}If you want to help please send the {2} from the{1}KSP Mod Admin intall dir to{1}mackerbal@mactee.de{1}or use the issue tracker{1}https://github.com/MacTee/KSP-Mod-Admin-aOS/issues", msg, Environment.NewLine, KSPMA_LOG_FILENAME);
                MessageBox.Show(displayMsg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log.AddErrorS(msg, ex);
            }

            Log.AddInfoS(string.Format("---> KSP MA v{0} closed <---{1}", VersionHelper.GetAssemblyVersion(true), Environment.NewLine));
        }

        public static void ShutDown()
        {
            IsShutDown = true;

            SaveAppConfig();
            SaveKSPConfig();
        }

        #region IMessageReceiver implementation

        /// <summary>
        /// Adds a message to the info message box.
        /// </summary>
        /// <param name="msg">The message to add.</param>
        public void AddMessage(string msg)
        {
            View.InvokeIfRequired(() => View.tbMessages.AppendText(msg + Environment.NewLine));
        }

        /// <summary>
        /// Adds a message to the info message box.
        /// </summary>
        /// <param name="msg">The info message to add.</param>
        public void AddInfo(string msg)
        {
            View.InvokeIfRequired(() => View.tbMessages.AppendText(msg + Environment.NewLine));
            Log.AddInfoS(msg);
        }

        /// <summary>
        /// Adds a message to the info message box.
        /// </summary>
        /// <param name="msg">The debug message to add.</param>
        public void AddDebug(string msg)
        {
            View.InvokeIfRequired(() => View.tbMessages.AppendText(msg + Environment.NewLine));
            Log.AddDebugS(msg);
        }

        /// <summary>
        /// Adds a message to the info message box.
        /// </summary>
        /// <param name="msg">The warning message to add.</param>
        public void AddWarning(string msg)
        {
            View.InvokeIfRequired(() => View.tbMessages.AppendText(msg + Environment.NewLine));
            Log.AddWarningS(msg);
        }

        /// <summary>
        /// Adds a message to the info message box.
        /// </summary>
        /// <param name="msg">The error message to add.</param>
        /// <param name="ex">The exception to add to the error message.</param>
        public void AddError(string msg, Exception ex = null)
        {
            View.InvokeIfRequired(() => View.tbMessages.AppendText(msg + Environment.NewLine));
            Log.AddErrorS(msg, ex);
        }

        #endregion


        /// <summary>
        /// Loads all available languages.
        /// </summary>
        protected static void LoadLanguages()
        {
            // Try load languages.
            bool langLoadFailed = false;
            try
            {
                Localizer.GlobalInstance.DefaultLanguage = "eng";
                langLoadFailed = !Localizer.GlobalInstance.LoadLanguages(KSPPathHelper.GetPath(KSPPaths.LanguageFolder), true);
            }
            catch
            {
                langLoadFailed = true;
            }

            if (langLoadFailed)
            {
                MessageBox.Show("Can not load languages!" + Environment.NewLine + "Fall back to defalut language: English", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Localizer.GlobalInstance.Clear();
            }
        }

        /// <summary>
        /// This method gets called when your Controller should be initialized.
        /// Perform additional initialization of your UserControl here.
        /// </summary>
        protected static void Initialize()
        {
            Messenger.AddListener(Instance);

            EventDistributor.AsyncTaskStarted += AsyncTaskStarted;
            EventDistributor.AsyncTaskDone += AsyncTaskDone;
            EventDistributor.LanguageChanged += LanguageChanged;
            EventDistributor.KSPRootChanging += KSPRootChanging;
            EventDistributor.KSPRootChanged += KSPRootChanged;

            LoadConfigs();

            LoadPlugins();

            OptionsController.AvailableLanguages = Localizer.GlobalInstance.AvailableLanguages;
            OptionsController.SelectedLanguage = Localizer.GlobalInstance.CurrentLanguage;

            LoadSiteHandler();

            if (!KSPPathHelper.IsKSPInstallFolder(OptionsController.SelectedKSPPath))
            {
                frmWelcome dlg = new frmWelcome();
                if (dlg.ShowDialog(View) != DialogResult.OK)
                {
                    View.Close();
                    return;
                }

                OptionsController.AddKSPPath(dlg.KSPPath);
                OptionsController.SelectedKSPPath = dlg.KSPPath;
            }

            // auto mod update check.
            OptionsController.Check4ModUpdates(true);
        }

        /// <summary>
        /// Deletes the old content of the log file when file size is above 1mb and creates a new log file.
        /// </summary>
        protected static void SetupLogFile()
        {
            //#if DEBUG
            Log.GlobalInstance.LogMode = LogMode.All;
            //#else
            //            Log.GlobalInstance.LogMode = LogMode.WarningsAndErrors;
            //#endif
            try
            {
                string logPath = Path.Combine(Application.StartupPath, KSPMA_LOG_FILENAME);
                if (File.Exists(logPath))
                {
                    FileInfo fInfo = new FileInfo(logPath);
                    if (fInfo.Length > 2097152) // > 2mb
                        File.Delete(logPath);
                }

                Log.GlobalInstance.FullPath = logPath;

                Log.AddInfoS(string.Format("---> KSP MA v{0} started <---", VersionHelper.GetAssemblyVersion(true)));
            }
            catch (Exception)
            {
                MessageBox.Show(Messages.MSG_CANT_CREATE_KSPMA_LOG);
                Log.GlobalInstance.LogMode = LogMode.None;
            }
        }

        /// <summary>
        /// Loads the AppConfig & KSPConfig.
        /// </summary>
        protected static void LoadConfigs()
        {
            Messenger.AddInfo(Messages.MSG_LOADING_KSPMA_SETTINGS);
            string path = KSPPathHelper.GetPath(KSPPaths.AppConfig);
            if (File.Exists(path))
            {
                if (AdminConfig.Load(path))
                {
                    Messenger.AddInfo(Messages.MSG_DONE);

                    // LoadKSPConfig will be started by KSPPathChange event.
                    //if (KSPPathHelper.IsKSPInstallFolder(OptionsController.SelectedKSPPath))
                    //    LoadKSPConfig();
                }
                else
                {
                    Messenger.AddInfo(Messages.MSG_LOADING_KSPMA_SETTINGS_FAILED);
                }
            }
            else
            {
                Messenger.AddInfo(Messages.MSG_KSPMA_SETTINGS_NOT_FOUND);
            }

            DeleteOldAppConfigs();
        }

        /// <summary>
        /// Loads the KSPConfig from the selected KSP folder.
        /// </summary>
        protected static void LoadKSPConfig()
        {
            ModSelectionController.ClearMods();

            string configPath = KSPPathHelper.GetPath(KSPPaths.KSPConfig);
            if (File.Exists(configPath))
            {
                Messenger.AddInfo(Messages.MSG_LOADING_KSP_MOD_CONFIGURATION);
                List<ModNode> mods = new List<ModNode>();
                KSPConfig.Load(configPath, ref mods);
                ModSelectionController.AddMods(mods.ToArray());
                ModSelectionController.SortModSelection();
            }
            else
            {
                Messenger.AddInfo(Messages.MSG_KSP_MOD_CONFIGURATION_NOT_FOUND);
            }

            ModSelectionController.RefreshCheckedStateAllMods();
            Messenger.AddInfo(Messages.MSG_DONE);
        }

        /// <summary>
        /// Deletes older config paths and files.
        /// </summary>
        protected static void DeleteOldAppConfigs()
        {
            string path = KSPPathHelper.GetPath(KSPPaths.AppConfig);
            string[] dirs = Directory.GetDirectories(Path.GetDirectoryName(path));
            foreach (string dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                        continue;

                    Directory.Delete(dir, true);
                }
                catch (Exception)
                { }
            }
        }

        /// <summary>
        /// Saves the AppConfig to "c:\ProgramData\..."
        /// </summary>
        protected static void SaveAppConfig()
        {
            try
            {
                Messenger.AddInfo(Messages.MSG_SAVING_KSPMA_SETTINGS);
                string path = KSPPathHelper.GetPath(KSPPaths.AppConfig);
                if (path != string.Empty && Directory.Exists(Path.GetDirectoryName(path)))
                    AdminConfig.Save(path);
                else
                    Messenger.AddError(Messages.MSG_KSPMA_SETTINGS_PATH_INVALID);
            }
            catch (Exception ex)
            {
                Messenger.AddError(Messages.MSG_ERROR_DURING_SAVING_KSPMA_SETTINGS, ex);
                ShowAdminRightsDlg(ex);
            }
        }

        /// <summary>
        /// Saves the KSPConfig to the selected KSP folder.
        /// </summary>
        public static void SaveKSPConfig()
        {
            try
            {
                string path = KSPPathHelper.GetPath(KSPPaths.KSPConfig);
                if (path != string.Empty && Directory.Exists(Path.GetDirectoryName(path)))
                {
                    Messenger.AddInfo(Messages.MSG_SAVING_KSP_MOD_SETTINGS);
                    KSPConfig.Save(path, ModSelectionController.Mods);
                }
                else
                    Messenger.AddError(Messages.MSG_KSP_MOD_SETTINGS_PATH_INVALID);
            }
            catch (Exception ex)
            {
                Messenger.AddError(Messages.MSG_ERROR_DURING_SAVING_KSP_MOD_SETTINGS, ex);
                ShowAdminRightsDlg(ex);
            }
        }

        /// <summary>
        /// Shows a MessageBox with the info, that KSP MA needs admin rights if KSP is installed to c:\Programme
        /// </summary>
        /// <param name="ex">The message of the Exception will be displayed too.</param>
        protected static void ShowAdminRightsDlg(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format(Messages.MSG_ERROR_MESSAGE_0, ex.Message));
            sb.AppendLine();
            sb.AppendLine(Messages.MSG_ACCESS_DENIED_DIALOG_MESSAGE);
            MessageBox.Show(View, sb.ToString(), Messages.MSG_TITLE_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Loads and registers all SiteHandler.
        /// </summary>
        protected static void LoadSiteHandler()
        {
            //Add default SiteHandler
            var siteHandlers = PluginLoader.GetPlugins<ISiteHandler>(new[] {Assembly.GetExecutingAssembly()});
            foreach (ISiteHandler handler in siteHandlers)
                SiteHandlerManager.RegisterSiteHandler(handler);

            //Add additional SiteHandlers
            siteHandlers = PluginLoader.LoadPlugins<ISiteHandler>(KSPPathHelper.GetPath(KSPPaths.KSPMA_Plugins));
            foreach (ISiteHandler handler in siteHandlers)
                SiteHandlerManager.RegisterSiteHandler(handler);
        }

        /// <summary>
        /// Loads all Plugins for KSP Mod Admin.
        /// </summary>
        protected static void LoadPlugins()
        {
            try
            {
                var plugins = PluginLoader.LoadPlugins<IKSPMAPlugin>(KSPPathHelper.GetPath(KSPPaths.KSPMA_Plugins));
                foreach (IKSPMAPlugin plugin in plugins)
                {
                    TabView[] tabViews = plugin.GetMainTabViews();
                    foreach (TabView tabView in tabViews)
                    {
                        if (!mAddedTabViews.ContainsKey(tabView.TabName))
                        {
                            TabPage tabPage = new TabPage();
                            tabPage.Text = tabView.TabName;
                            tabPage.Controls.Add(tabView.TabUserControl);
                            tabView.TabUserControl.Dock = DockStyle.Fill;
                            if (tabView.TabIcon != null)
                            {
                                View.TabControl.ImageList.Images.Add(tabView.TabIcon);
                                tabPage.ImageIndex = View.TabControl.ImageList.Images.Count - 1;
                            }
                            View.TabControl.TabPages.Add(tabPage);

                            mAddedTabViews.Add(tabView.TabName, tabView);
                        }
                        else
                        {
                            Messenger.AddError(string.Format("Plugin loading error: TabView \"{0}\" already exists!", tabView.TabName));
                        }
                    }

                    tabViews = plugin.GetOptionTabViews();
                    foreach (TabView tabView in tabViews)
                    {
                        if (!mAddedTabViews.ContainsKey(tabView.TabName))
                        {
                            TabPage tabPage = new TabPage();
                            tabPage.Text = tabView.TabName;
                            tabPage.Controls.Add(tabView.TabUserControl);
                            tabView.TabUserControl.Dock = DockStyle.Fill; ;
                            OptionsController.View.TabControl.TabPages.Add(tabPage);

                            mAddedTabViews.Add(tabView.TabName, tabView);
                        }
                        else
                        {
                            Messenger.AddError(string.Format("Plugin loading error: Option TabView \"{0}\" already exists!", tabView.TabName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Messenger.AddError(string.Format("Plugin loading error: \"{0}\"", ex.Message), ex);
            }
        }
        protected static Dictionary<string, TabView> mAddedTabViews = new Dictionary<string, TabView>();

        #region EventDistributor callback functions.

        /// <summary>
        /// Callback function for the AsyncTaskStarted event.
        /// Should disable all controls of the BaseView.
        /// </summary>
        protected static void AsyncTaskDone(object sender)
        {
            View.cbKSPPath.Enabled = false;
        }

        /// <summary>
        /// Callback function for the AsyncTaskDone event.
        /// Should enable all controls of the BaseView.
        /// </summary>
        protected static void AsyncTaskStarted(object sender)
        {
            View.cbKSPPath.Enabled = true;
        }

        /// <summary>
        /// Callback function for the LanguageChanged event.
        /// Translates all controls of the BaseView.
        /// </summary>
        protected static void LanguageChanged(object sender)
        {
            // translates the controls of the view.
            ControlTranslator.TranslateControls(Localizer.GlobalInstance, View as Control, OptionsController.SelectedLanguage);

            foreach (TabView addedTabView in mAddedTabViews.Values)
            {
                TabPage tabPage = addedTabView.TabUserControl.Parent as TabPage;
                if (tabPage != null)
                    tabPage.Text = addedTabView.TabName;
            }
        }

        /// <summary>
        /// Event handler for the KSPPathChanging event from OptionsController.
        /// </summary>
        /// <param name="oldKSPPath">The last selected KSP path.</param>
        /// <param name="newKSPPath">The new selected ksp path.</param>
        protected static void KSPRootChanging(string oldKSPPath, string newKSPPath)
        {
            if (!string.IsNullOrEmpty(oldKSPPath))
                SaveKSPConfig();
        }

        /// <summary>
        /// Event handler for the KSPPathChanged event from OptionsController.
        /// </summary>
        /// <param name="kspPath">the new selected ksp path.</param>
        protected static void KSPRootChanged(string kspPath)
        {
            if (!string.IsNullOrEmpty(kspPath))
                LoadKSPConfig();

            _SelectedKSPPath = kspPath;
        }

        #endregion
    }
}
