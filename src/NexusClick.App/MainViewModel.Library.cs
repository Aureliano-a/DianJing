using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using NexusClick.Engine;

namespace NexusClick.App;

public sealed partial class MainViewModel
{
    private int _macroPageIndex;
    public int MacroPageIndex { get=>_macroPageIndex; set=>Set(ref _macroPageIndex,value); }
    private MacroLibrary? _library;
    private MacroLibraryData _libraryData=new(1,new(),new());
    private MacroGroup? _selectedGroup;
    private MacroLibraryItem? _selectedLibraryItem;
    private string _groupName="", _itemName="", _librarySearch="";
    public string LibrarySearch { get=>_librarySearch; set { if(_librarySearch==value) return; Set(ref _librarySearch,value); RefreshLibraryItems(); } }
    public ICommand ClearLibrarySearchCommand { get; private set; }=null!;
    public ObservableCollection<MacroGroup> LibraryGroups { get; } = new();
    public ObservableCollection<MacroLibraryItem> LibraryItems { get; } = new();
    public ObservableCollection<MacroLibraryItem> LibraryPageItems { get; } = new();
    private int _libraryPage;
    private const int LibraryPageSize=3;
    public string LibraryPageLabel => $"{_libraryPage+1} / {Math.Max(1,(LibraryItems.Count+LibraryPageSize-1)/LibraryPageSize)}";
    public ICommand LibraryPreviousCommand { get; private set; }=null!;
    public ICommand LibraryNextCommand { get; private set; }=null!;
    private void NavigateLibraryPage(int delta) {
        var next=Math.Clamp(_libraryPage+delta,0,Math.Max(0,(LibraryItems.Count-1)/LibraryPageSize));
        if(next==_libraryPage) return;
        SelectedLibraryItem=null; _libraryPage=next; ShowLibraryPage();
    }
    private void ShowLibraryPage() {
        _libraryPage=Math.Clamp(_libraryPage,0,Math.Max(0,(LibraryItems.Count-1)/LibraryPageSize));
        LibraryPageItems.Clear(); foreach(var item in LibraryItems.Skip(_libraryPage*LibraryPageSize).Take(LibraryPageSize)) LibraryPageItems.Add(item);
        PropertyChanged?.Invoke(this,new(nameof(LibraryPageLabel)));
    }
    public MacroGroup? SelectedGroup { get=>_selectedGroup; set { Set(ref _selectedGroup,value); GroupName=value?.Name??""; RefreshLibraryItems(); } }
    public MacroLibraryItem? SelectedLibraryItem { get=>_selectedLibraryItem; set { Set(ref _selectedLibraryItem,value); ItemName=value?.Macro.Name??""; } }
    public string GroupName { get=>_groupName; set=>Set(ref _groupName,value); }
    public string ItemName { get=>_itemName; set=>Set(ref _itemName,value); }
    public ICommand CreateGroupCommand { get; private set; }=null!;
    public ICommand RenameGroupCommand { get; private set; }=null!;
    public ICommand SaveToLibraryCommand { get; private set; }=null!;
    public ICommand OpenLibraryItemCommand { get; private set; }=null!;
    public ICommand RenameLibraryItemCommand { get; private set; }=null!;
    private void InitializeLibrary()
    {
        LibraryPreviousCommand=new RelayCommand(()=>NavigateLibraryPage(-1));
        LibraryNextCommand=new RelayCommand(()=>NavigateLibraryPage(1));
        ClearLibrarySearchCommand=new RelayCommand(()=>LibrarySearch="");
        CreateGroupCommand=new RelayCommand(()=>LibraryAction(CreateGroup));
        RenameGroupCommand=new RelayCommand(()=>LibraryAction(RenameGroup));
        SaveToLibraryCommand=new RelayCommand(()=>LibraryAction(SaveToLibrary));
        OpenLibraryItemCommand=new RelayCommand(()=>LibraryAction(OpenLibraryItem));
        RenameLibraryItemCommand=new RelayCommand(()=>LibraryAction(RenameLibraryItem));
        if(Environment.GetEnvironmentVariable("DIANJING_DISABLE_SETTINGS_PERSISTENCE")=="1") return;
        try
        {
            _library=new MacroLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DianJing","macro-library.json"));
            _libraryData=_library.Load(); RefreshGroups(null);
        }
        catch(Exception ex) { _library=null; Notice="宏列表读取失败，请检查本地文件："+ex.Message; }
    }
    private void LibraryAction(Action action)
    {
        try { if(_library is null) throw new InvalidOperationException("宏列表不可用，请检查本地文件。"); if(IsTaskActive() || _recording) throw new InvalidOperationException("请先停止任务或录制。"); action(); }
        catch(Exception ex) { Notice="宏列表操作失败："+ex.Message; }
    }
    private static string CheckedName(string name) => MacroLibrary.ValidName(name) ? name.Trim() : throw new ArgumentException("名称须为 1–80 个非控制字符。");
    private void CommitLibrary(MacroLibraryData data,Guid? group,Guid? item=null)
    {
        _library!.Save(data); _libraryData=data; RefreshGroups(group);
        if(item.HasValue) { var index=LibraryItems.ToList().FindIndex(x=>x.Macro.Id==item); if(index>=0) { _libraryPage=index/LibraryPageSize; ShowLibraryPage(); SelectedLibraryItem=LibraryItems[index]; } }
    }
    private void RefreshGroups(Guid? id)
    {
        LibraryGroups.Clear(); foreach(var group in _libraryData.Groups) LibraryGroups.Add(group);
        SelectedGroup=LibraryGroups.FirstOrDefault(x=>x.Id==id) ?? LibraryGroups.FirstOrDefault();
        RefreshLibraryItems();
    }
    private void RefreshLibraryItems()
    {
        var selectedId=SelectedLibraryItem?.Macro.Id;
        LibraryItems.Clear(); SelectedLibraryItem=null;
        if(_selectedGroup is not null) foreach(var item in _libraryData.Items.Where(x=>x.GroupId==_selectedGroup.Id && (string.IsNullOrWhiteSpace(LibrarySearch) || x.Macro.Name.Contains(LibrarySearch.Trim(),StringComparison.CurrentCultureIgnoreCase)))) LibraryItems.Add(item);
        if(selectedId.HasValue) SelectedLibraryItem=LibraryItems.FirstOrDefault(x=>x.Macro.Id==selectedId);
        ShowLibraryPage();
    }
    private void CreateGroup()
    {
        var name=CheckedName(GroupName); var group=new MacroGroup(Guid.NewGuid(),name);
        var data=_libraryData with { Groups=_libraryData.Groups.Append(group).ToList() };
        CommitLibrary(data,group.Id); Notice="宏分组已创建。";
    }
    private void RenameGroup()
    {
        var group=SelectedGroup ?? throw new InvalidOperationException("请先选择分组。");
        var updated=group with { Name=CheckedName(GroupName) };
        CommitLibrary(_libraryData with { Groups=_libraryData.Groups.Select(x=>x.Id==group.Id?updated:x).ToList() },group.Id);
        Notice="宏分组已重命名。";
    }
    private void SaveToLibrary()
    {
        var document=new MacroDocument(1,_macroId,CheckedName(MacroName),BuildMacroPlan()); document.Validate();
        var group=SelectedGroup ?? _libraryData.Groups.FirstOrDefault();
        var groups=_libraryData.Groups;
        if(group is null)
        {
            group=new MacroGroup(Guid.NewGuid(),Localization.Instance.Translate("默认分组"));
            groups=groups.Append(group).ToList();
        }
        var items=_libraryData.Items.Where(x=>x.Macro.Id!=document.Id).Append(new MacroLibraryItem(group.Id,document)).ToList();
        CommitLibrary(_libraryData with { Groups=groups, Items=items },group.Id,document.Id); LibrarySearch=""; SelectedLibraryItem=LibraryItems.FirstOrDefault(x=>x.Macro.Id==document.Id); Notice="宏已保存到列表。";
    }
    private void OpenLibraryItem()
    {
        var doc=SelectedLibraryItem?.Macro ?? throw new InvalidOperationException("请先选择宏。");
        if(doc.Plan.Steps.Any(s=>s.Kind==MacroStepKind.Key && (s.VirtualKey is 0x77 or 0x7A or 0x7B || s.VirtualKey==_startHotkeyVk))) throw new InvalidDataException("宏中包含当前快捷键或保留键。");
        _macroId=doc.Id; MacroName=doc.Name; MacroRepeats=doc.Plan.RepeatCount.ToString(); MacroTimeout=doc.Plan.MaximumWallSeconds.ToString();
        ApplyDraft(new MacroDraft(doc.Plan.Steps),-1); MacroPageIndex=0; Notice="宏已打开，可在步骤编辑中修改。";
    }
    private void RenameLibraryItem()
    {
        var item=SelectedLibraryItem ?? throw new InvalidOperationException("请先选择宏。");
        var updated=item with { Macro=item.Macro with { Name=CheckedName(ItemName) } };
        CommitLibrary(_libraryData with { Items=_libraryData.Items.Select(x=>x.Macro.Id==item.Macro.Id?updated:x).ToList() },item.GroupId,item.Macro.Id);
        if(_macroId==item.Macro.Id) MacroName=updated.Macro.Name;
        Notice="宏已重命名。";
    }
}
