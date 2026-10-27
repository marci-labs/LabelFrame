using Android.App;
using Android.Content;
using Android.Provider;
using Android.Views;
using Android.Widget;
using LabelFrame.AndroidHost.Updates;

namespace LabelFrame.AndroidHost;

/// <summary>展示后台下载、系统确认安装与持久恢复状态。</summary>
public sealed partial class MainActivity
{
    private TextView? _updateState;
    private TextView? _updateDetails;
    private Button? _installUpdate;
    private Button? _manualRecovery;
    private bool _preparingUpdate;
    private string? _updateActionError;

    private ScrollView BuildUpdatesScreen()
    {
        var content = ScrollColumn();
        content.AddView(Header(L(Resource.String.update_title)));
        content.AddView(Subtle(L(Resource.String.update_policy)));
        content.AddView(Spacing(16));
        content.AddView(FieldLabel(L(Resource.String.device_version_label)));
        content.AddView(TextView(HostInfo.GetVersion(this), 22, ColorText, true));
        content.AddView(Spacing(16));
        _updateState = TextView("", 18, ColorText, true);
        _updateDetails = TextView("", 13, ColorTextSecondary);
        content.AddView(_updateState);
        content.AddView(Spacing(8));
        content.AddView(_updateDetails);
        content.AddView(Spacing(24));
        content.AddView(ActionButton(L(Resource.String.update_permission), () =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                StartActivity(new Intent(Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + PackageName)));
        }, ViewGroup.LayoutParams.MatchParent));
        content.AddView(Spacing(12));
        _installUpdate = DangerButton(L(Resource.String.update_install), ConfirmUpdate, ViewGroup.LayoutParams.MatchParent);
        content.AddView(_installUpdate);
        content.AddView(Spacing(12));
        content.AddView(ActionButton(L(Resource.String.update_system_confirmation), () =>
        {
            if (!AndroidUpdateInstaller.ShowConfirmation(this))
                Toast.MakeText(this, L(Resource.String.update_no_confirmation), ToastLength.Long)?.Show();
        }, ViewGroup.LayoutParams.MatchParent));
        content.AddView(Spacing(12));
        content.AddView(ActionButton(L(Resource.String.update_retry_health), () => RunAsync(async () =>
        {
            if (AndroidUpdateInstaller.Current is { } installer) await installer.RetryHealthAsync(CancellationToken.None);
            RefreshUpdateStatus();
        }), ViewGroup.LayoutParams.MatchParent));
        content.AddView(Spacing(12));
        _manualRecovery = DangerButton(L(Resource.String.update_manual_recovery), ConfirmManualRecovery, ViewGroup.LayoutParams.MatchParent);
        content.AddView(_manualRecovery);
        return WrapScroll(content);
    }

    private void RefreshUpdateStatus()
    {
        if (_updateState is null || _updateDetails is null) return;
        var download = AndroidUpdateMonitor.Current;
        var transaction = AndroidUpdateInstaller.Journal;
        var state = transaction.Current;
        var phase = transaction.Fault is not null ? "repair" : state is not null && (transaction.NeedsRecovery || download.Phase != "ready")
            ? state.Phase : download.Phase;
        _updateState.Text = phase switch
        {
            "ready" => L(Resource.String.update_ready),
            "downloading" or "checking" => L(Resource.String.update_downloading),
            "preparing" or "submitted" => L(Resource.String.update_submitted),
            "confirmation" => L(Resource.String.update_confirmation),
            "recovering" => L(Resource.String.update_recovering),
            "succeeded" => L(Resource.String.update_succeeded),
            "manually-resumed" => L(Resource.String.update_manually_resumed),
            "cancelled" or "rejected" => L(Resource.String.update_cancelled),
            "repair" or "unknown" => L(Resource.String.update_repair),
            "unsupported" => L(Resource.String.update_unsupported),
            "unconfigured" => L(Resource.String.update_unconfigured),
            "failed" or "blocked" => L(Resource.String.update_failed),
            _ => L(Resource.String.update_idle)
        };
        var target = download.Release?.Package.VersionName ?? state?.TargetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        var reason = transaction.Fault ?? (transaction.NeedsRecovery ? state?.Reason
            : download.Phase is "failed" or "blocked" ? download.Reason : null);
        _updateDetails.Text = L(Resource.String.update_details, target,
            MaintenanceGate.Shared.IsFrozen ? L(Resource.String.update_frozen) : L(Resource.String.update_unfrozen))
            + "\n" + L(OperatingSystem.IsAndroidVersionAtLeast(26) && PackageManager!.CanRequestPackageInstalls()
                ? Resource.String.update_permission_granted : Resource.String.update_permission_missing)
            + (reason is null ? "" : "\n" + reason);
        if (AndroidUpdateInstaller.Current is { HasUnknownRoute: true })
            _updateDetails.Text += "\n" + L(Resource.String.update_unknown_route);
        if (state?.ManualRecovery is not null || AndroidUpdateInstaller.Current?.RouteAcknowledgement is not null)
            _updateDetails.Text += "\n" + L(Resource.String.update_manually_resumed);
        if (_updateActionError is not null)
            _updateDetails.Text += "\n" + _updateActionError;
        if (_installUpdate is not null) _installUpdate.Enabled = !_preparingUpdate && download.Phase == "ready" && !transaction.NeedsRecovery;
        if (_manualRecovery is not null)
        {
            _manualRecovery.Visibility = transaction.Fault is null && (transaction.NeedsRecovery
                ? state?.Phase is "unknown" or "confirmation" or "repair" or "rejected"
                : AndroidUpdateInstaller.Current?.HasUnknownRoute == true) ? ViewStates.Visible : ViewStates.Gone;
            _manualRecovery.Enabled = !_preparingUpdate;
        }
    }

    private void ConfirmManualRecovery()
    {
        if (_preparingUpdate) return;
        var transaction = AndroidUpdateInstaller.Journal;
        var transactionId = transaction.NeedsRecovery ? transaction.Current?.Id : null;
        var fields = ScrollColumn();
        fields.AddView(Subtle(L(transactionId is null ? Resource.String.update_manual_route_risk : Resource.String.update_manual_install_risk)));
        fields.AddView(FieldLabel(L(Resource.String.update_admin_token)));
        var token = new EditText(this) { InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword };
        fields.AddView(token);
        fields.AddView(FieldLabel(L(Resource.String.update_recovery_reason)));
        var reason = new EditText(this) { InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine };
        fields.AddView(reason);
        var dialog = new AlertDialog.Builder(this)!
            .SetTitle(L(Resource.String.update_manual_recovery))!
            .SetView(WrapScroll(fields))!
            .SetNegativeButton(L(Resource.String.update_not_now), (_, _) => { })!
            .SetPositiveButton(L(Resource.String.update_accept_risk), (_, _) =>
            {
                var password = token.Text ?? "";
                var explanation = reason.Text ?? "";
                token.Text = "";
                RunAsync(async () =>
                {
                    _preparingUpdate = true;
                    RefreshUpdateStatus();
                    try
                    {
                        var installer = AndroidUpdateInstaller.Current ?? throw new InvalidOperationException(L(Resource.String.update_service_not_ready));
                        await installer.RecoverManuallyAsync(transactionId, password, explanation,
                            () => _autoRefresh && _current == Screen.Updates && !IsFinishing && !IsDestroyed, CancellationToken.None);
                    }
                    finally { password = ""; _preparingUpdate = false; RefreshUpdateStatus(); }
                });
            })!.Create()!;
        dialog.DismissEvent += (_, _) => token.Text = "";
        dialog.Show();
        var confirm = dialog.GetButton((int)DialogButtonType.Positive)!;
        confirm.Enabled = false;
        _refreshHandler.PostDelayed(() => { if (dialog.IsShowing) confirm.Enabled = true; }, 3000);
    }

    private void ConfirmUpdate()
    {
        if (_preparingUpdate || AndroidUpdateMonitor.Current.Release is not { } release) return;
        var dialog = new AlertDialog.Builder(this)!
            .SetTitle(L(Resource.String.update_install))!
            .SetMessage(L(Resource.String.update_confirm_impact, HostInfo.GetVersion(this), release.Package.VersionName))!
            .SetNegativeButton(L(Resource.String.update_not_now), (_, _) => { })!
            .SetPositiveButton(L(Resource.String.update_install), (_, _) => RunAsync(PrepareUpdateAsync))!.Create()!;
        dialog.Show();
        var confirm = dialog.GetButton((int)DialogButtonType.Positive)!;
        confirm.Enabled = false;
        _refreshHandler.PostDelayed(() => { if (dialog.IsShowing) confirm.Enabled = true; }, 3000);
    }

    private async Task PrepareUpdateAsync()
    {
        if (_preparingUpdate || !_autoRefresh || _current != Screen.Updates) return;
        _preparingUpdate = true;
        _updateActionError = null;
        RefreshUpdateStatus();
        try
        {
            var installer = AndroidUpdateInstaller.Current ?? throw new InvalidOperationException(L(Resource.String.update_service_not_ready));
            await installer.PrepareAsync(() => _autoRefresh && _current == Screen.Updates && !IsFinishing && !IsDestroyed, CancellationToken.None);
        }
        catch (Exception error)
        {
            _updateActionError = error.Message;
            throw;
        }
        finally { _preparingUpdate = false; RefreshUpdateStatus(); }
    }
}
