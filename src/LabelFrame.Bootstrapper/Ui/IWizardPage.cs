namespace LabelFrame.Bootstrapper.Ui;

/// <summary>向导分页接口：进入时刷新内容；离开前校验是否可继续。</summary>
internal interface IWizardPage
{
    /// <summary>进入该页时刷新（如确认页按最新问卷答案重建组件清单）。</summary>
    void OnEnter();

    /// <summary>是否允许进入下一页（false 时 <paramref name="reason"/> 给出中文提示）。</summary>
    bool CanProceed(out string? reason);
}
