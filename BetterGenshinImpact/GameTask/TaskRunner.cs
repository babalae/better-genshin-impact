using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// �����Զ�������ķ�ʽִ�����ⷽ��
/// </summary>
public class TaskRunner
{
    private readonly ILogger<TaskRunner> _logger = App.GetLogger<TaskRunner>();

    // private readonly DispatcherTimerOperationEnum _timerOperation = DispatcherTimerOperationEnum.None;

    private readonly string _name = string.Empty;

    public TaskRunner()
    {
    }

    // public TaskRunner(DispatcherTimerOperationEnum timerOperation)
    // {
    //     _timerOperation = timerOperation;
    // }
    
    /// <summary>
    /// ������������������
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public async Task RunCurrentAsync(Func<Task> action)
    {
        // ����
        var hasLock = await TaskSemaphore.WaitAsync(0);
        if (!hasLock)
        {
            _logger.LogError("��������ʧ�ܣ���ǰ�������������еĶ��������벻Ҫ�ظ�ִ������");
            return;
        }
        try
        {
            _logger.LogInformation("�� {Text}", _name + "����������");

            // ��ʼ��
            Init();
            
            CancellationContext.Instance.Set();
            RunnerContext.Instance.Clear();

            await action();
        }
        catch (NormalEndException e)
        {
            Notify.Event(NotificationEvent.TaskCancel).Success("notification.message.taskCancelNormal");
            _logger.LogInformation("�����ж�:{Msg}", e.Message);
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // ����ִ��ʱ���׳��쳣����ִֹ��
                throw;
            }
        }
        catch (TaskCanceledException e)
        {
            Notify.Event(NotificationEvent.TaskCancel).Success("notification.message.taskCancelManual");
            _logger.LogInformation("�����ж�:{Msg}", "����ȡ��");
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // ����ִ��ʱ���׳��쳣����ִֹ��
                throw;
            }
        }
        catch (Exception e)
        {
            Notify.Event(NotificationEvent.TaskError).Error("notification.error.taskExecution", e);
            _logger.LogError(e.Message);
            _logger.LogDebug(e.StackTrace);
        }
        finally
        {
            End();
            _logger.LogInformation("�� {Text}", _name + "�������");

            CancellationContext.Instance.Clear();
            RunnerContext.Instance.Clear();

            // �ͷ���
            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    public void FireAndForget(Func<Task> action)
    {
        Task.Run(() => RunCurrentAsync(action));
    }

    public async Task RunThreadAsync(Func<Task> action)
    {
        await Task.Run(() => RunCurrentAsync(action));
    }

    public async Task RunSoloTaskAsync(ISoloTask soloTask)
    {
        // û������ʱ��������
        bool waitForMainUi = soloTask.Name != "�Զ���ʥ�ٻ�" && !soloTask.Name.Contains("�Զ�����") && !soloTask.Name.Contains("�ľ�Σս");
        await ScriptService.StartGameTask(waitForMainUi);
        await Task.Run(() => RunCurrentAsync(async () => await soloTask.Start(CancellationContext.Instance.Cts.Token)));
    }

    public void Init()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("����������ҳ��������ͼ����ʹ�ñ�����"); });
            throw new NormalEndException("����������ҳ��������ͼ����ʹ�ñ�����");
        }

        // ���ʵʱ���񴥷���
        TaskTriggerDispatcher.Instance().ClearTriggers();

        
        // ����ԭ�񴰿�
        var maskWindow = MaskWindow.Instance();
        SystemControl.ActivateWindow();
        maskWindow.Invoke(maskWindow.Show);
    }

    public void End()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            return;
        }
        
        // ��ԭʵʱ���񴥷���
        TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());

        VisionContext.Instance().DrawContent.ClearAll();
    }

}

