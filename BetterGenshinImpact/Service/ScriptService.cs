using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.FarmingPlan;
using BetterGenshinImpact.GameTask.TaskProgress;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public partial class ScriptService : IScriptService
{
    private readonly ILogger<ScriptService> _logger = App.GetLogger<ScriptService>();
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    private static bool IsCurrentHourEqual(string input)
    {
        // ���Խ������ַ���ת��Ϊ����
        if (int.TryParse(input, out int hour))
        {
            // ��֤Сʱ�Ƿ��ںϷ���Χ�ڣ�0-23��
            if (hour is >= 0 and <= 23)
            {
                // ��ȡ��ǰСʱ��
                int currentHour = DateTime.Now.Hour;
                // �ж��Ƿ����
                return currentHour == hour;
            }
        }

        // �����������ֻ򲻺Ϸ������� false
        return false;
    }
    public bool ShouldSkipTask(ScriptGroupProject project)
    {

        if (project.GroupInfo is { Config.PathingConfig.Enabled: true } )
        {
            if (IsCurrentHourEqual(project.GroupInfo.Config.PathingConfig.SkipDuring))
            {
                _logger.LogInformation($"{project.Name}�����ѵ���ִֹ��ʱ�Σ���������");
                return true;
            }

            var tcc = project.GroupInfo.Config.PathingConfig.TaskCycleConfig;
            if (tcc.Enable)
            {
                int index = tcc.GetExecutionOrder(DateTime.Now);
                if (index == -1)
                {
                    _logger.LogInformation($"{project.Name}�������ò����������ý�����Ч����������ִ�У�");
                }
                else if (index != tcc.Index)
                {
                    _logger.LogInformation($"{project.Name}�����Ѿ�����ִ�����ڣ���ǰֵ${index}!=����ֵ${tcc.Index}����������������");
                    return true;
                }
               
            }
            
        }

        if (TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig.Enabled)
        {
            try
            {
                var task = PathingTask.BuildFromFilePath(Path.Combine(MapPathingViewModel.PathJsonPath, project.FolderName, project.Name));
                string message;
                if (FarmingStatsRecorder.IsDailyFarmingLimitReached(task.FarmingInfo,out message))
                {
                    _logger.LogInformation($"{project.Name}:{message},����������");
                    return true;
                }
            }
            catch (Exception e)
            {
                TaskControl.Logger.LogError($"���ع滮ͳ���쳣��{e.Message}");
            }

            
        }
        
        
        
        return false; // ������
    }
    public async Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null,TaskProgress? taskProgress = null)
    {
        groupName ??= "Ĭ��";

        var list = ReloadScriptProjects(projectList);
        
        //�ָ���ʱ��������־
        foreach (var scriptGroupProject in projectList)
        {
            scriptGroupProject.SkipFlag = false;
        }
        

        // // ���JS �ű�������Ƿ������ʱ������
        // var jsProjects = ExtractJsProjects(list);
        // if (!hasTimer && jsProjects.Count > 0)
        // {
        //     var codeList = await ReadCodeList(jsProjects);
        //     hasTimer = HasTimerOperation(codeList);
        // }

        // û����ʱ��������ͼ��
        await StartGameTask();

        if (!string.IsNullOrEmpty(groupName))
        {
            // if (hasTimer)
            // {
            //     _logger.LogInformation("������ {Name} ����ʵʱ�����������", groupName);
            // }

            _logger.LogInformation("������ {Name} ������ɣ���{Cnt}���ű�����ʼִ��", groupName, list.Count);
        }

        // var timerOperation = hasTimer ? DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty : DispatcherTimerOperationEnum.UseSelfCaptureImage;

        
        bool fisrt = true;
        await new TaskRunner()
            .RunThreadAsync(async () =>
            {
                var stopwatch = new Stopwatch();
                int projectIndex = -1;
                foreach (var project in list)
                {
                    projectIndex++;
                    if (taskProgress != null && taskProgress.Next != null)
                    {
                        if (taskProgress.Next.Index>projectIndex)
                        {
                            continue;
                        }
                        taskProgress.Next = null;
                    }

                    if (project is {SkipFlag:true})
                    {
                        continue;
                    }
                    if (ShouldSkipTask(project))
                    {
                        continue;
                    }
                    //�¿����
                    await _blessingOfTheWelkinMoonTask.Start(CancellationContext.Instance.Cts.Token);
                    if (project.Status != "Enabled")
                    {
                        _logger.LogInformation("�ű� {Name} ״̬Ϊ���ã�����ִ��", project.Name);
                        continue;
                    }

                    if (CancellationContext.Instance.Cts.IsCancellationRequested)
                    {
                        _logger.LogInformation("ִ�б�ȡ��");
                        break;
                    }

                    
                    if (fisrt)
                    {
                        fisrt = false;
                        Notify.Event(NotificationEvent.GroupStart).Success("notification.message.configGroupStartNamed", groupName);
                    }

                    if (taskProgress!=null)
                    {
                        taskProgress.CurrentScriptGroupProjectInfo = new TaskProgress.ScriptGroupProjectInfo
                        {
                            Name = project.Name,
                            FolderName = project.FolderName
                            ,Index = projectIndex
                            ,GroupName = taskProgress?.CurrentScriptGroupName ?? ""
                        };
                        TaskProgressManager.SaveTaskProgress(taskProgress);
                    }
                    for (var i = 0; i < project.RunNum; i++)
                    {
                        try
                        {
                            TaskTriggerDispatcher.Instance().ClearTriggers();


                            _logger.LogInformation("------------------------------");

                            stopwatch.Reset();
                            stopwatch.Start();
                            
                            await ExecuteProject(project);

                            //���ִ��ʱ��ʱ�ж�
                            if (ShouldSkipTask(project))
                            {
                                continue;
                            }
                        }
                        catch (NormalEndException e)
                        {
                            throw;
                        }
                        catch (TaskCanceledException e)
                        {
                            _logger.LogInformation("ȡ��ִ��������: {Msg}", e.Message);
                            throw;
                        }
                        catch (Exception e)
                        {
                            _logger.LogDebug(e, "ִ�нű�ʱ�����쳣");
                            _logger.LogError("ִ�нű�ʱ�����쳣: {Msg}", e.Message);
                            if (taskProgress!=null && taskProgress.CurrentScriptGroupProjectInfo!=null )
                            {
                                taskProgress.CurrentScriptGroupProjectInfo.Status = 2;
                            }
                        }
                        finally
                        {
                            stopwatch.Stop();
                            var elapsedTime = TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds);
                            // _logger.LogDebug("�� �ű�ִ�н���: {Name}, ��ʱ: {ElapsedMilliseconds} ����", project.Name, stopwatch.ElapsedMilliseconds);
                            _logger.LogInformation("�� �ű�ִ�н���: {Name}, ��ʱ: {Minutes}��{Seconds:0.000}��", project.Name,
                                elapsedTime.Hours * 60 + elapsedTime.Minutes, elapsedTime.TotalSeconds % 60);
                            _logger.LogInformation("------------------------------");
                        }

                        await Task.Delay(2000);
                    }

                    if (taskProgress != null)
                    {
                        if (taskProgress.CurrentScriptGroupProjectInfo!=null )
                        {
                            taskProgress.CurrentScriptGroupProjectInfo.TaskEnd = true;
                            taskProgress.CurrentScriptGroupProjectInfo.EndTime = DateTime.Now;
                            if (taskProgress.CurrentScriptGroupProjectInfo.Status == 1)
                            {
                                taskProgress.ConsecutiveFailureCount = 0;
                                taskProgress.LastSuccessScriptGroupProjectInfo =
                                    taskProgress.CurrentScriptGroupProjectInfo;
                                taskProgress.LastScriptGroupName =taskProgress.CurrentScriptGroupName;
                            }
                            //�ۼ�����ʧ�ܴ���
                            if (taskProgress.CurrentScriptGroupProjectInfo.Status == 2)
                            {
                                taskProgress.ConsecutiveFailureCount++;
                            }

                            taskProgress?.History?.Add(taskProgress.CurrentScriptGroupProjectInfo);
                            TaskProgressManager.SaveTaskProgress(taskProgress);
                        }

                        //�쳣�ﵽһ�δ���������bgi
                        var autoconfig = TaskContext.Instance().Config.OtherConfig.AutoRestartConfig;
                        if (autoconfig.Enabled && taskProgress.ConsecutiveFailureCount >= autoconfig.FailureCount)
                        {
                            _logger.LogInformation("�������������δԤ�ڵ��쳣���Զ�����bgi");
                            Notify.Event(NotificationEvent.GroupEnd).Error("notification.error.unexpectedError");
                            if (autoconfig.RestartGameTogether 
                                && TaskContext.Instance().Config.GenshinStartConfig.LinkedStartEnabled 
                                && TaskContext.Instance().Config.GenshinStartConfig.AutoEnterGameEnabled)
                            {
                                SystemControl.CloseGame();
                                Thread.Sleep(2000);
                            }

                            SystemControl.RestartApplication(["--TaskProgress",taskProgress.Name]);
                        }

                    }
                }
            });

        // ��ԭ��ʱ��
        TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());
        
        if (!string.IsNullOrEmpty(groupName))
        {
            _logger.LogInformation("������ {Name} ִ�н���", groupName);
        }

        if (!fisrt)
        {
            Notify.Event(NotificationEvent.GroupEnd).Success("notification.message.configGroupEndNamed", groupName);
        }

        if (taskProgress != null)
        {
            taskProgress.Next = null;
        }

    }

    private List<ScriptGroupProject> ReloadScriptProjects(IEnumerable<ScriptGroupProject> projectList)
    {
        var list = new List<ScriptGroupProject>();
        foreach (var project in projectList)
        {
            if (project.Type == "Javascript")
            {
                var newProject = new ScriptGroupProject(new ScriptProject(project.FolderName));
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
            }
            else if (project.Type == "KeyMouse")
            {
                var newProject = ScriptGroupProject.BuildKeyMouseProject(project.Name);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
            }
            else if (project.Type == "Pathing")
            {
                var newProject = ScriptGroupProject.BuildPathingProject(project.Name, project.FolderName);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
                // hasTimer = true;
            }
            else if (project.Type == "Shell")
            {
                var newProject = ScriptGroupProject.BuildShellProject(project.Name);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
                // hasTimer = true;
            }
        }

        return list;
    }

    private void CopyProjectProperties(ScriptGroupProject source, ScriptGroupProject target)
    {
        target.Status = source.Status;
        target.Schedule = source.Schedule;
        target.RunNum = source.RunNum;
        target.JsScriptSettingsObject = source.JsScriptSettingsObject;
        target.GroupInfo = source.GroupInfo;
        target.AllowJsNotification = source.AllowJsNotification;
        target.SkipFlag = source.SkipFlag;
    }

    // private List<ScriptProject> ExtractJsProjects(List<ScriptGroupProject> list)
    // {
    //     var jsProjects = new List<ScriptProject>();
    //     foreach (var project in list)
    //     {
    //         if (project is { Type: "Javascript", Project: not null })
    //         {
    //             jsProjects.Add(project.Project);
    //         }
    //     }
    //
    //     return jsProjects;
    // }

    private async Task ExecuteProject(ScriptGroupProject project)
    {
        TaskContext.Instance().CurrentScriptProject = project;
        if (project.Type == "Javascript")
        {
            if (project.Project == null)
            {
                throw new Exception("Project Ϊ��");
            }

            _logger.LogInformation("�� ��ʼִ��JS�ű�: {Name}", project.Name);
            await project.Run();
        }
        else if (project.Type == "KeyMouse")
        {
            _logger.LogInformation("�� ��ʼִ�м���ű�: {Name}", project.Name);
            await project.Run();
        }
        else if (project.Type == "Pathing")
        {
            _logger.LogInformation("�� ��ʼִ�е�ͼ׷������: {Name}", project.Name);
            await project.Run();
        }
        else if (project.Type == "Shell")
        {
            _logger.LogInformation("�� ��ʼִ��shell: {Name}", project.Name);
            await project.Run();
        }
    }

    private async Task<List<string>> ReadCodeList(List<ScriptProject> list)
    {
        var codeList = new List<string>();
        foreach (var project in list)
        {
            var code = await project.LoadCode();
            codeList.Add(code);
        }

        return codeList;
    }

    private bool HasTimerOperation(IEnumerable<string> codeList)
    {
        return codeList.Any(code => DispatcherAddTimerRegex().IsMatch(code));
    }

    [GeneratedRegex(@"^(?!\s*\/\/)\s*dispatcher\.\s*addTimer", RegexOptions.Multiline)]
    private static partial Regex DispatcherAddTimerRegex();


    public static async Task StartGameTask(bool waitForMainUi = true)
    {
        // û����ʱ��������ͼ��
        var homePageViewModel = App.GetService<HomePageViewModel>();
        if (!homePageViewModel!.TaskDispatcherEnabled)
        {
            await homePageViewModel.OnStartTriggerAsync();

            if (waitForMainUi)
            {
                await Task.Run(async () =>
                {
                    await Task.Delay(200);
                    var first = true;
                    var sw = Stopwatch.StartNew();
                    var loseFocusCount = 0;
                    while (true)
                    {
                        if (!homePageViewModel.TaskDispatcherEnabled || !TaskContext.Instance().IsInitialized)
                        {
                            continue;
                        }

                        var content = TaskControl.CaptureToRectArea();
                        if (Bv.IsInMainUi(content) || Bv.IsInAnyClosableUi(content))
                        {
                            return;
                        }

                        if (first)
                        {
                            first = false;
                            TaskControl.Logger.LogInformation("��ǰ������Ϸ�����棬�ȴ������������ִ������...");
                            TaskControl.Logger.LogInformation("������Ѿ�����Ϸ�ڵ��������棬�������˳���ǰ���棨ESC��������30��󽫳����Զ����Ե��������棬ʹ��ǰ�����ܹ��������У�");
                        }

                        await Task.Delay(500);
                        if (sw.Elapsed.TotalSeconds >= 30)
                        {
                            //��ֹ��������Ϸ����ΪһЩԭ��ʧ��������һֱ��ס
                            if (!SystemControl.IsGenshinImpactActiveByProcess())
                            {
                                loseFocusCount++;
                                if (loseFocusCount>50 && loseFocusCount<100)
                                {
                                    SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
                                }
                                SystemControl.ActivateWindow();
                            }

                            //��������Ϸ������������Ϸ���棬���޷��Զ����ţ����ﳢ���ƶ�����Ϸ����
                            if (sw.Elapsed.TotalSeconds < 200)
                            {
                                GlobalMethod.MoveMouseTo(300, 300);
                            }


                        }
                    }
                });
            }
        }
    }
}

