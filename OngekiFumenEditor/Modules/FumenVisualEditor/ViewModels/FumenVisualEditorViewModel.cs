using Caliburn.Micro;
using Gemini.Framework;
using OngekiFumenEditor.Base;
using OngekiFumenEditor.Modules.AudioPlayerToolViewer;
using OngekiFumenEditor.Modules.FumenBulletPalleteListViewer;
using OngekiFumenEditor.Modules.FumenMetaInfoBrowser;
using OngekiFumenEditor.Modules.FumenVisualEditor.Base;
using OngekiFumenEditor.Modules.FumenVisualEditor.Models;
using OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels.Dialogs;
using OngekiFumenEditor.Modules.FumenVisualEditorSettings;
using OngekiFumenEditor.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels
{
    [Export(typeof(FumenVisualEditorViewModel))]
    public partial class FumenVisualEditorViewModel : PersistedDocument
    {
        private EditorProjectDataModel editorProjectData = new EditorProjectDataModel();
        public EditorProjectDataModel EditorProjectData
        {
            get
            {
                return editorProjectData;
            }
            set
            {
                Set(ref editorProjectData, value);
                TotalDurationHeight = value.AudioDuration;
                Setting = EditorProjectData.EditorSetting;
                Fumen = EditorProjectData.Fumen;
                if (IoC.Get<IAudioPlayerToolViewer>() is IAudioPlayerToolViewer audioPlayerToolViewer && IsActive)
                    audioPlayerToolViewer.Editor = this;
            }
        }

        public EditorSetting Setting
        {
            get
            {
                return EditorProjectData.EditorSetting;
            }
            set
            {
                this.RegisterOrUnregisterPropertyChangeEvent(EditorProjectData.EditorSetting, value, OnSettingPropertyChanged);
                EditorProjectData.EditorSetting = value;
                if (IoC.Get<IFumenVisualEditorSettings>() is IFumenVisualEditorSettings editorSettings && IsActive)
                    editorSettings.Setting = value;
                NotifyOfPropertyChange(() => Setting);
            }
        }

        private void OnSettingPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(EditorSetting.UnitCloseSize):
                    RecalculateXUnitSize();
                    Redraw(RedrawTarget.XGridUnitLines);
                    break;
                case nameof(EditorSetting.CurrentDisplayTimePosition):
                    ScrollViewerVerticalOffset = TGridCalculator.ConvertTGridToY(Setting.CurrentDisplayTimePosition, this);
                    break;
                case nameof(EditorSetting.BeatSplit):
                //case nameof(EditorSetting.BaseLineY):
                    Redraw(RedrawTarget.TGridUnitLines | RedrawTarget.ScrollBar);
                    break;
                case nameof(EditorSetting.EditorDisplayName):
                    if (IoC.Get<WindowTitleHelper>() is WindowTitleHelper title)
                        title.TitleContent = base.DisplayName;
                    break;
                case nameof(EditorSetting.XGridMaxUnit):
                    RecalculateXUnitSize();
                    Redraw(RedrawTarget.OngekiObjects | RedrawTarget.XGridUnitLines);
                    break;
                default:
                    break;
            }
        }

        public OngekiFumen Fumen
        {
            get
            {
                return EditorProjectData.Fumen;
            }
            set
            {
                if (EditorProjectData.Fumen is not null)
                    EditorProjectData.Fumen.BpmList.OnChangedEvent -= OnBPMListChanged;
                if (value is not null)
                    value.BpmList.OnChangedEvent += OnBPMListChanged;
                EditorProjectData.Fumen = value;
                OnFumenObjectLoaded();
                Redraw(RedrawTarget.All);
                NotifyOfPropertyChange(() => Fumen);
            }
        }

        private double canvasWidth = default;
        public double CanvasWidth
        {
            get => canvasWidth;
            set
            {
                Set(ref canvasWidth, value);
                RecalculateXUnitSize();
            }
        }
        private double canvasHeight = default;
        public double CanvasHeight
        {
            get => canvasHeight;
            set
            {
                Set(ref canvasHeight, value);
            }
        }

        public ObservableCollection<XGridUnitLineViewModel> XGridUnitLineLocations { get; } = new();
        public ObservableCollection<TGridUnitLineViewModel> TGridUnitLineLocations { get; } = new();
        public ObservableCollection<IEditorDisplayableViewModel> EditorViewModels { get; } = new();
        public bool IsDragging { get; private set; }
        public bool IsMouseDown { get; private set; }

        private void OnFumenObjectLoaded()
        {
            IoC.Get<IFumenMetaInfoBrowser>().Fumen = Fumen;
            IoC.Get<IFumenBulletPalleteListViewer>().Fumen = Fumen;
        }

        #region Document New/Save/Load

        protected override async Task DoNew()
        {
            var dialogViewModel = new EditorProjectSetupDialogViewModel();
            var result = await IoC.Get<IWindowManager>().ShowDialogAsync(dialogViewModel);
            if (result != true)
            {
                Log.LogInfo($"用户无法完成新建项目向导，关闭此编辑器");
                await TryCloseAsync(false);
                return;
            }
            EditorProjectData = dialogViewModel.EditorProjectData;
            Redraw(RedrawTarget.All);
            Log.LogInfo($"FumenVisualEditorViewModel DoNew()");
            await Dispatcher.Yield();
        }

        protected override async Task DoLoad(string filePath)
        {
            using var _ = StatusBarHelper.BeginStatus("Editor project file loading : " + filePath);
            Log.LogInfo($"FumenVisualEditorViewModel DoLoad() : {filePath}");
            var projectData = await EditorProjectDataUtils.TryLoadFromFileAsync(filePath);
            EditorProjectData = projectData;
            Redraw(RedrawTarget.All);
        }

        protected override async Task DoSave(string filePath)
        {
            using var _ = StatusBarHelper.BeginStatus("Fumen saving : " + filePath);
            Log.LogInfo($"FumenVisualEditorViewModel DoSave() : {filePath}");
            await EditorProjectDataUtils.TrySaveToFileAsync(filePath, EditorProjectData);
        }

        #endregion

        #region Activation

        protected override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (IoC.Get<IFumenVisualEditorSettings>() is IFumenVisualEditorSettings editorSettings)
                editorSettings.Setting = Setting;
            if (IoC.Get<IAudioPlayerToolViewer>() is IAudioPlayerToolViewer audioPlayerTool)
                audioPlayerTool.Editor = this;
            return base.OnActivateAsync(cancellationToken);
        }

        protected override Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            if (IoC.Get<IFumenVisualEditorSettings>() is IFumenVisualEditorSettings editorSettings && editorSettings.Setting == Setting)
                editorSettings.Setting = default;
            if (IoC.Get<IAudioPlayerToolViewer>() is IAudioPlayerToolViewer audioPlayerTool && audioPlayerTool.Editor == this)
                audioPlayerTool.Editor = default;
            return base.OnDeactivateAsync(close, cancellationToken);
        }

        #endregion

        public void AddOngekiObject(DisplayObjectViewModelBase viewModel)
        {
            if (viewModel is IEditorDisplayableViewModel m)
                m.OnObjectCreated(viewModel.ReferenceOngekiObject, this);
            Fumen.AddObject(viewModel.ReferenceOngekiObject);
            EditorViewModels.Add(viewModel);
            //Log.LogInfo($"create new display object: {viewModel.ReferenceOngekiObject.GetType().Name}");
        }
    }
}
