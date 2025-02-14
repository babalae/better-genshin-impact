# BetterGI项目结构图
贡献者：https://github.com/Because66666
下面为完整的项目结构图，截止2025.2.12下午3点。对代码的摘要理解在`BetterGI项目结构图.md`中。
```
BetterGenshinImpact/
├── App.xaml.cs
├── AssemblyInfo.cs
├── Assets/
│   ├── Audios/
│   ├── Fonts/
│   ├── Highlighting/
│   ├── Images/
│   │   ├── Anniversary/
│   ├── Model/
│   │   ├── Common/
│   │   ├── Domain/
│   │   ├── Fish/
│   │   ├── PaddleOCR/
│   │   │   ├── ch_PP-OCRv4_det/
│   │   │   ├── ch_PP-OCRv4_rec/
│   │   │   ├── ch_ppocr_mobile_v2.0_cls/
│   │   ├── World/
│   │   └── Yap/
│   └── Strings/
├── Core/
│   ├── Config/
│   │   ├── AllConfig.cs
│   │   ├── CommonConfig.cs
│   │   ├── GenshinStartConfig.cs
│   │   ├── Global.cs
│   │   ├── HotKeyConfig.cs
│   │   ├── KeyBindingsConfig.cs
│   │   ├── MacroConfig.cs
│   │   ├── MaskWindowConfig.cs
│   │   ├── OneDragonFlowConfig.cs
│   │   ├── PathingConditionConfig.cs
│   │   ├── PathingPartyConfig.cs
│   │   ├── RecordConfig.cs
│   │   ├── RectConfig.cs
│   │   └── ScriptConfig.cs
│   ├── Monitor/
│   │   ├── DirectInputMonitor.cs
│   │   └── MouseKeyMonitor.cs
│   ├── Recognition/
│   │   ├── OCR/
│   │   │   ├── IOcrService.cs
│   │   │   ├── OcrFactory.cs
│   │   │   ├── PaddleOcrResultExtension.cs
│   │   │   └── PaddleOcrService.cs
│   │   ├── ONNX/
│   │   │   ├── BgiSessionOption.cs
│   │   │   ├── BgiYoloV8Predictor.cs
│   │   │   ├── BgiYoloV8PredictorFactory.cs
│   │   │   ├── SVTR/
│   │   │   │   ├── ITextInference.cs
│   │   │   │   ├── PickTextInference.cs
│   │   │   │   └── TextInferenceFactory.cs
│   │   │   └── YOLO/
│   │   │   │   └── Predictor.cs
│   │   ├── OcrEngineTypes.cs
│   │   ├── OpenCv/
│   │   │   ├── ArithmeticHelper.cs
│   │   │   ├── CommonExtension.cs
│   │   │   ├── CommonRecognition.cs
│   │   │   ├── ContoursHelper.cs
│   │   │   ├── CropHelper.cs
│   │   │   ├── FeatureMatch/
│   │   │   │   ├── DescriptorMatcherType.cs
│   │   │   │   ├── Feature2DType.cs
│   │   │   │   ├── FeatureMatcher.cs
│   │   │   │   ├── FeatureStorage.cs
│   │   │   │   └── KeyPointFeatureBlockHelper.cs
│   │   │   ├── MatchTemplateHelper.cs
│   │   │   ├── Model/
│   │   │   │   └── KeyPointFeatureBlock.cs
│   │   │   ├── OpenCvCommonHelper.cs
│   │   │   └── ResizeHelper.cs
│   │   ├── RecognitionObject.cs
│   │   └── RecognitionTypes.cs
│   ├── Recorder/
│   │   ├── DirectInputCalibration.cs
│   │   ├── GlobalKeyMouseRecord.cs
│   │   ├── KeyMouseMacroPlayer.cs
│   │   ├── KeyMouseRecorder.cs
│   │   └── Model/
│   │   │   ├── KeyMouseScript.cs
│   │   │   ├── KeyMouseScriptInfo.cs
│   │   │   └── MacroEvent.cs
│   ├── Script/
│   │   ├── CancellationContext.cs
│   │   ├── Dependence/
│   │   │   ├── AutoPathingScript.cs
│   │   │   ├── Dispatcher.cs
│   │   │   ├── Genshin.cs
│   │   │   ├── GlobalMethod.cs
│   │   │   ├── KeyMouseScript.cs
│   │   │   ├── LimitedFile.cs
│   │   │   ├── Log.cs
│   │   │   ├── Model/
│   │   │   │   ├── RealTimeTimer.cs
│   │   │   │   ├── SoloTask.cs
│   │   │   │   └── TimerConfig/
│   │   │   │   │   └── AutoPickExternalConfig.cs
│   │   │   └── Simulator/
│   │   │   │   └── PostMessage.cs
│   │   ├── EngineExtend.cs
│   │   ├── Group/
│   │   │   ├── ScriptGroup.cs
│   │   │   ├── ScriptGroupConfig.cs
│   │   │   └── ScriptGroupProject.cs
│   │   ├── Project/
│   │   │   ├── Author.cs
│   │   │   ├── Manifest.cs
│   │   │   └── ScriptProject.cs
│   │   ├── ScriptRepoUpdater.cs
│   │   ├── Utils/
│   │   │   └── ScriptUtils.cs
│   │   └── WebView/
│   │   │   └── RepoWebBridge.cs
│   └── Simulator/
│   │   ├── Extensions/
│   │   │   ├── Enums.cs
│   │   │   ├── InputSimulatorExtension.cs
│   │   │   ├── PostMessageSimulatorExtension.cs
│   │   │   └── SimulateKeyHelper.cs
│   │   ├── MouseEventSimulator.cs
│   │   ├── PostMessageSimulator.cs
│   │   └── Simulation.cs
├── GameTask/
│   ├── AutoCook/
│   │   ├── AutoCookTrigger.cs
│   │   └── AutoPickConfig.cs
│   ├── AutoDomain/
│   │   ├── AutoDomainConfig.cs
│   │   ├── AutoDomainParam.cs
│   │   └── AutoDomainTask.cs
│   ├── AutoFight/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   ├── AutoFightAssets.cs
│   │   ├── AutoFightConfig.cs
│   │   ├── AutoFightContext.cs
│   │   ├── AutoFightParam.cs
│   │   ├── AutoFightTask.cs
│   │   ├── Config/
│   │   │   ├── CombatAvatar.cs
│   │   │   └── DefaultAutoFightConfig.cs
│   │   ├── Model/
│   │   │   ├── Avatar.cs
│   │   │   ├── AvatarMacro.cs
│   │   │   └── CombatScenes.cs
│   │   ├── OneKeyFightTask.cs
│   │   └── Script/
│   │   │   ├── CombatCommand.cs
│   │   │   ├── CombatScript.cs
│   │   │   ├── CombatScriptBag.cs
│   │   │   ├── CombatScriptParser.cs
│   │   │   └── Method.cs
│   ├── AutoFishing/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   │   ├── bait/
│   │   │   └── AutoFishingAssets.cs
│   │   ├── AutoFishingConfig.cs
│   │   ├── AutoFishingImageRecognition.cs
│   │   ├── AutoFishingTrigger.cs
│   │   ├── Model/
│   │   │   ├── BaitType.cs
│   │   │   ├── BigFishType.cs
│   │   │   ├── FishType.cs
│   │   │   ├── Fishpond.cs
│   │   │   └── OneFish.cs
│   │   ├── RodInput.cs
│   │   └── RodNet.cs
│   ├── AutoGeniusInvokation/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   │   ├── dice/
│   │   │   │   └── other/
│   │   │   ├── AutoGeniusInvokationAssets.cs
│   │   ├── AutoGeniusInvokationConfig.cs
│   │   ├── AutoGeniusInvokationTask.cs
│   │   ├── Config/
│   │   │   ├── CharacterCard.cs
│   │   │   └── DefaultTcgConfig.cs
│   │   ├── GeniusInvokationControl.cs
│   │   ├── GeniusInvokationTaskParam.cs
│   │   ├── Model/
│   │   │   ├── ActionCommand.cs
│   │   │   ├── ActionEnum.cs
│   │   │   ├── Character.cs
│   │   │   ├── CharacterStatusEnum.cs
│   │   │   ├── Duel.cs
│   │   │   ├── ElementalType.cs
│   │   │   ├── RollPhaseDice.cs
│   │   │   ├── RoundStrategy.cs
│   │   │   └── Skill.cs
│   │   └── ScriptParser.cs
│   ├── AutoMusicGame/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   └── AutoMusicAssets.cs
│   │   ├── AutoAlbumTask.cs
│   │   ├── AutoMusicGameConfig.cs
│   │   ├── AutoMusicGameParam.cs
│   │   └── AutoMusicGameTask.cs
│   ├── AutoPathing/
│   │   ├── CameraRotateTask.cs
│   │   ├── Handler/
│   │   │   ├── ActionFactory.cs
│   │   │   ├── AutoFightHandler.cs
│   │   │   ├── CombatScriptHandler.cs
│   │   │   ├── ElementalCollectHandler.cs
│   │   │   ├── ElementalSkillHandler.cs
│   │   │   ├── IActionHandler.cs
│   │   │   ├── MiningHandler.cs
│   │   │   ├── NahidaCollectHandler.cs
│   │   │   ├── NormalAttackHandler.cs
│   │   │   ├── PickAroundHandler.cs
│   │   │   └── UpDownGrabLeaf.cs
│   │   ├── Model/
│   │   │   ├── Enum/
│   │   │   │   ├── ActionEnum.cs
│   │   │   │   ├── MoveModeEnum.cs
│   │   │   │   ├── PathingTaskType.cs
│   │   │   │   └── WaypointType.cs
│   │   │   ├── PathingTask.cs
│   │   │   ├── PathingTaskConfig.cs
│   │   │   ├── PathingTaskInfo.cs
│   │   │   ├── Waypoint.cs
│   │   │   └── WaypointForTrack.cs
│   │   ├── Navigation.cs
│   │   ├── PathExecutor.cs
│   │   ├── PathRecorder.cs
│   │   ├── Suspend/
│   │   │   ├── ISuspendable.cs
│   │   │   └── PathExecutorSuspend.cs
│   │   └── TrapEscaper.cs
│   ├── AutoPick/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   └── AutoPickAssets.cs
│   │   ├── AutoPickConfig.cs
│   │   ├── AutoPickTrigger.cs
│   │   └── PickOcrEngineEnum.cs
│   ├── AutoSkip/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   ├── AutoSkipAssets.cs
│   │   │   ├── HangoutConfig.cs
│   │   ├── AutoSkipConfig.cs
│   │   ├── AutoSkipTrigger.cs
│   │   ├── AutoTrackTask.cs
│   │   ├── ExpeditionTask.cs
│   │   ├── Model/
│   │   │   ├── AutoTrackParam.cs
│   │   │   ├── ExpeditionCharacterCard.cs
│   │   │   ├── HangoutOption.cs
│   │   │   ├── PaddleOcrResultRect.cs
│   │   │   └── SelectChatOptionTypes.cs
│   │   └── OneKeyExpeditionTask.cs
│   ├── AutoTrackPath/
│   │   ├── Assets/
│   │   ├── AutoTrackPathParam.cs
│   │   ├── AutoTrackPathTask.cs
│   │   ├── Model/
│   │   │   ├── GiPath.cs
│   │   │   ├── GiPathPoint.cs
│   │   │   └── GiWorldPosition.cs
│   │   ├── MovementControl.cs
│   │   ├── PathPointRecorder.cs
│   │   ├── TpConfig.cs
│   │   └── TpTask.cs
│   ├── AutoWood/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   └── AutoWoodAssets.cs
│   │   ├── AutoWoodConfig.cs
│   │   ├── AutoWoodTask.cs
│   │   ├── Utils/
│   │   │   └── Login3rdParty.cs
│   │   └── WoodTaskParam.cs
│   ├── CaptureContent.cs
│   ├── Common/
│   │   ├── BgiVision/
│   │   │   ├── BvImage.cs
│   │   │   ├── BvSimpleOperation.cs
│   │   │   └── BvStatus.cs
│   │   ├── Element/
│   │   │   └── Assets/
│   │   │   │   ├── 1920x1080/
│   │   │   │   ├── ElementAssets.cs
│   │   │   │   ├── Json/
│   │   │   │   ├── MapAssets.cs
│   │   │   │   └── MapLazyAssets.cs
│   │   ├── Exceptions/
│   │   │   ├── NormalEndException.cs
│   │   │   ├── RetryException.cs
│   │   │   ├── RetryNoCountException.cs
│   │   │   └── TpPointNotActivate.cs
│   │   ├── Job/
│   │   │   ├── ArtifactSalvageTask.cs
│   │   │   ├── BlessingOfTheWelkinMoonTask.cs
│   │   │   ├── ChooseTalkOptionTask.cs
│   │   │   ├── ClaimBattlePassRewardsTask.cs
│   │   │   ├── ClaimEncounterPointsRewardsTask.cs
│   │   │   ├── ClaimMailRewardsTask.cs
│   │   │   ├── GoToAdventurersGuildTask.cs
│   │   │   ├── GoToCraftingBenchTask.cs
│   │   │   ├── ReturnMainUiTask.cs
│   │   │   ├── ScanPickTask.cs
│   │   │   └── SwitchPartyTask.cs
│   │   ├── Map/
│   │   │   ├── BigMap.cs
│   │   │   ├── Camera/
│   │   │   │   ├── CameraOrientationFromGia.cs
│   │   │   │   └── CameraOrientationFromLimint.cs
│   │   │   ├── CameraOrientation.cs
│   │   │   ├── CharacterOrientation.cs
│   │   │   ├── EntireMap.cs
│   │   │   ├── EntireMapOperation.cs
│   │   │   └── MapCoordinate.cs
│   │   ├── NewRetry.cs
│   │   ├── TaskControl.cs
│   │   └── YoloManager.cs
│   ├── GameLoading/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   └── GameLoadingAssets.cs
│   │   └── GameLoading.cs
│   ├── GameTaskManager.cs
│   ├── ISoloTask.cs
│   ├── ITaskTrigger.cs
│   ├── LogParse/
│   │   ├── LogParse.cs
│   │   ├── LogParseConfig.cs
│   │   ├── MoraStatistics.cs
│   │   ├── NoLoginException.cs
│   │   ├── TravelsDiaryDetailManager.cs
│   │   └── YsHttp.cs
│   ├── Macro/
│   │   ├── QuickEnhanceArtifactMacro.cs
│   │   └── TurnAroundMacro.cs
│   ├── Model/
│   │   ├── Area/
│   │   │   ├── Converter/
│   │   │   │   ├── ConvertRes.cs
│   │   │   │   ├── INodeConverter.cs
│   │   │   │   ├── ScaleConverter.cs
│   │   │   │   └── TranslationConverter.cs
│   │   │   ├── DesktopRegion.cs
│   │   │   ├── GameCaptureRegion.cs
│   │   │   ├── ImageRegion.cs
│   │   │   └── Region.cs
│   │   ├── BaseAssets.cs
│   │   ├── BaseIndependentTask.cs
│   │   ├── BaseTaskParam.cs
│   │   ├── Enum/
│   │   │   ├── DispatcherCaptureModeEnum.cs
│   │   │   └── DispatcherTimerOperationEnum.cs
│   │   ├── IndependentTaskEnum.cs
│   │   ├── RectArea.cs
│   │   └── SystemInfo.cs
│   ├── Placeholder/
│   │   └── PlaceholderTrigger.cs
│   ├── QucikBuy/
│   │   └── QuickBuyTask.cs
│   ├── QuickForge/
│   │   └── QuickForgeTask.cs
│   ├── QuickSereniteaPot/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   └── QuickSereniteaPotAssets.cs
│   │   └── QuickSereniteaPotTask.cs
│   ├── QuickTeleport/
│   │   ├── Assets/
│   │   │   ├── 1920x1080/
│   │   │   └── QuickTeleportAssets.cs
│   │   ├── QuickTeleportConfig.cs
│   │   └── QuickTeleportTrigger.cs
│   ├── RunnerContext.cs
│   ├── SystemControl.cs
│   ├── TaskContext.cs
│   ├── TaskRunner.cs
│   ├── TaskTriggerDispatcher.cs
│   └── UseActiveCode/
│   │   └── UseActiveCodeTask.cs
├── Genshin/
│   ├── Paths/
│   │   ├── GameExePath.cs
│   │   ├── RegistryGameLocator.cs
│   │   └── UnityLogGameLocator.cs
│   ├── Settings/
│   │   ├── GenshinRegistry.cs
│   │   ├── InputDataSettings.cs
│   │   ├── LanguageSettings.cs
│   │   ├── MainJson.cs
│   │   ├── OverrideController.cs
│   │   ├── ResolutionSettings.cs
│   │   └── SettingsContainer.cs
│   └── Settings2/
│   │   ├── GameSettingsChecker.cs
│   │   ├── GenshinGameInputSettings.cs
│   │   └── GenshinGameSettings.cs
├── GlobalUsing.cs
├── Helpers/
│   ├── AssertUtils.cs
│   ├── Crud/
│   │   ├── ICrudHelper.cs
│   │   └── JsonCrudHelper.cs
│   ├── DirectoryHelper.cs
│   ├── DpiAwareness/
│   │   ├── DpiAwarenessController.cs
│   │   └── DpiAwarenessExtension.cs
│   ├── DpiHelper.cs
│   ├── ExpandoObjectConverter.cs
│   ├── Extensions/
│   │   ├── BitmapExtension.cs
│   │   ├── BooleanExtension.cs
│   │   ├── ClickExtension.cs
│   │   ├── DependencyInjectionExtensions.cs
│   │   ├── PointExtension.cs
│   │   ├── RectCutExtension.cs
│   │   ├── RectExtension.cs
│   │   └── TaskExtension.cs
│   ├── Http/
│   │   └── ProxySpeedTester.cs
│   ├── MathHelper.cs
│   ├── ObjectUtils.cs
│   ├── OsVersionHelper.cs
│   ├── PrimaryScreen.cs
│   ├── RegexHelper.cs
│   ├── ResourceHelper.cs
│   ├── RuntimeHelper.cs
│   ├── ScriptObjectConverter.cs
│   ├── SecurityControlHelper.cs
│   ├── SemaphoreSlimParallel.cs
│   ├── SpeedTimer.cs
│   ├── StringUtils.cs
│   ├── TempManager.cs
│   ├── UIDispatcherHelper.cs
│   ├── Ui/
│   │   ├── FileTreeNodeHelper.cs
│   │   └── WindowHelper.cs
│   ├── UrlProtocolHelper.cs
│   └── User32Helper.cs
├── Hutao/
│   ├── HutaoNamedPipe.cs
│   ├── PipePacketCommand.cs
│   ├── PipePacketContentType.cs
│   ├── PipePacketHeader.cs
│   ├── PipePacketType.cs
│   └── PipeStreamExtension.cs
├── Markup/
│   ├── ConverterExtension.cs
│   └── ServiceLocatorExtension.cs
├── Model/
│   ├── Condition.cs
│   ├── ConditionDefinition.cs
│   ├── FileTreeNode{T}.cs
│   ├── HotKey.cs
│   ├── HotKeySettingModel.cs
│   ├── HotKeyTypeEnum.cs
│   ├── KeyBindingSettingModel.cs
│   ├── KeyMouseScriptItem.cs
│   ├── KeyboardHook.cs
│   ├── MaskButton.cs
│   ├── MouseHook.cs
│   ├── Notice.cs
│   ├── OneDragonTaskItem.cs
│   ├── SettingItem.cs
│   ├── Singleton.cs
│   ├── StatusItem.cs
│   └── UpdateOption.cs
├── Properties/
│   ├── PublishProfiles/
├── Service/
│   ├── ApplicationHostService.cs
│   ├── ConfigService.cs
│   ├── Interface/
│   │   ├── IConfigService.cs
│   │   ├── IScriptService.cs
│   │   └── IUpdateService.cs
│   ├── Notification/
│   │   ├── Converter/
│   │   │   ├── BaseDateTimeJsonConverter.cs
│   │   │   ├── DateTimeJsonConverter.cs
│   │   │   └── ImageToBase64Converter.cs
│   │   ├── Model/
│   │   │   ├── Base/
│   │   │   │   ├── DomainDetails.cs
│   │   │   │   ├── GeniusInvocationDetails.cs
│   │   │   │   └── ScriptDetails.cs
│   │   │   ├── BaseNotificationData.cs
│   │   │   ├── DomainNotificationData.cs
│   │   │   ├── Enum/
│   │   │   │   ├── NotificationEvent.cs
│   │   │   │   └── NotificationEventResult.cs
│   │   │   ├── GeniusInvocationNotificationData.cs
│   │   │   ├── NotificationTestResult.cs
│   │   │   └── ScriptNotificationData.cs
│   │   ├── NotificationConfig.cs
│   │   ├── NotificationService.cs
│   │   └── Notify.cs
│   ├── Notifier/
│   │   ├── Exception/
│   │   │   └── NotifierException.cs
│   │   ├── FeishuNotifier.cs
│   │   ├── Interface/
│   │   │   └── INotifier.cs
│   │   ├── NotifierManager.cs
│   │   ├── WebhookNotifier.cs
│   │   ├── WindowsUwpNotifier.cs
│   │   └── WorkWeixinNotifier.cs
│   ├── PageService.cs
│   ├── ScriptService.cs
│   └── UpdateService.cs
├── User/
│   ├── AutoFight/
│   │   └── 群友分享/
│   ├── AutoGeniusInvokation/
│   ├── AutoSkip/
├── View/
│   ├── Behavior/
│   │   └── RightClickSelectBehavior.cs
│   ├── CaptureTestWindow.xaml.cs
│   ├── Controls/
│   │   ├── CodeEditor/
│   │   │   ├── CodeBox.cs
│   │   │   └── JsonCodeBox.cs
│   │   ├── Draggable/
│   │   │   ├── Adorners/
│   │   │   │   ├── DoubleFormatConverter.cs
│   │   │   │   ├── ResizeRotateAdorner.cs
│   │   │   │   ├── ResizeRotateChrome.cs
│   │   │   │   ├── SizeAdorner.cs
│   │   │   │   └── SizeChrome.cs
│   │   │   ├── DesignerItemDecorator.cs
│   │   │   ├── MoveThumb.cs
│   │   │   ├── ResizeThumb.cs
│   │   │   ├── RotateThumb.cs
│   │   ├── HotKey/
│   │   │   └── HotKeyTextBox.cs
│   │   ├── KeyBindings/
│   │   │   └── KeyBindingTextBox.cs
│   │   ├── Style/
│   │   ├── TwoStateButton.cs
│   │   ├── Webview/
│   │   │   ├── WebpagePanel.cs
│   │   │   └── WebpageWindow.cs
│   │   ├── WpfUi/
│   │   └── WpfUiWindow.cs
│   ├── Converters/
│   │   ├── BooleanToEnableTextConverter.cs
│   │   ├── BooleanToVisibilityRevertConverter.cs
│   │   ├── InverseBooleanConverter.cs
│   │   └── NotNullConverter.cs
│   ├── Drawable/
│   │   ├── DrawContent.cs
│   │   ├── LineDrawable.cs
│   │   ├── RectDrawable.cs
│   │   ├── TextDrawable.cs
│   │   └── VisionContext.cs
│   ├── MainWindow.xaml.cs
│   ├── MaskWindow.xaml.cs
│   ├── Pages/
│   │   ├── CommonSettingsPage.xaml.cs
│   │   ├── HomePage.xaml.cs
│   │   ├── HotkeyPage.xaml.cs
│   │   ├── JsListPage.xaml.cs
│   │   ├── KeyBindingsSettingsPage.xaml.cs
│   │   ├── KeyMouseRecordPage.xaml.cs
│   │   ├── MacroSettingsPage.xaml.cs
│   │   ├── MapPathingPage.xaml.cs
│   │   ├── NotificationSettingsPage.xaml.cs
│   │   ├── OneDragon/
│   │   │   ├── CraftPage.xaml.cs
│   │   │   ├── DailyCommissionPage.xaml.cs
│   │   │   ├── DailyRewardPage.xaml.cs
│   │   │   ├── DomainPage.xaml.cs
│   │   │   ├── ForgingPage.xaml.cs
│   │   │   ├── LeyLineBlossomPage.xaml.cs
│   │   │   ├── MailPage.xaml.cs
│   │   │   ├── SereniteaPotPage.xaml.cs
│   │   │   └── TcgPage.xaml.cs
│   │   ├── OneDragonFlowPage.xaml.cs
│   │   ├── ScriptControlPage.xaml.cs
│   │   ├── TaskSettingsPage.xaml.cs
│   │   ├── TriggerSettingsPage.xaml.cs
│   │   └── View/
│   │   │   ├── PathingConfigView.xaml.cs
│   │   │   └── ScriptGroupConfigView.xaml.cs
│   ├── PickerWindow.xaml.cs
│   └── Windows/
│   │   ├── CheckUpdateWindow.xaml.cs
│   │   ├── Editable/
│   │   │   └── ScriptGroupProjectEditor.xaml.cs
│   │   ├── JsonMonoDialog.xaml.cs
│   │   ├── MapViewer.xaml.cs
│   │   └── PromptDialog.xaml.cs
└── ViewModel/
│   ├── IViewModel.cs
│   ├── MainWindowViewModel.cs
│   ├── MaskWindowViewModel.cs
│   ├── Message/
│   │   └── RefreshDataMessage.cs
│   ├── NotifyIconViewModel.cs
│   ├── Pages/
│   │   ├── CommonSettingsPageViewModel.cs
│   │   ├── HomePageViewModel.cs
│   │   ├── HotKeyPageViewModel.cs
│   │   ├── JsListViewModel.cs
│   │   ├── KeyBindingsSettingsPageViewModel.cs
│   │   ├── KeyMouseRecordPageViewModel.cs
│   │   ├── MacroSettingsPageViewModel.cs
│   │   ├── MapPathingViewModel.cs
│   │   ├── NotificationSettingsPageViewModel.cs
│   │   ├── OneDragon/
│   │   │   ├── CraftViewModel.cs
│   │   │   ├── DailyCommissionViewModel.cs
│   │   │   ├── DailyRewardViewModel.cs
│   │   │   ├── DomainViewModel.cs
│   │   │   ├── ForgingViewModel.cs
│   │   │   ├── LeyLineBlossomViewModel.cs
│   │   │   ├── MailViewModel.cs
│   │   │   ├── OneDragonBaseViewModel.cs
│   │   │   ├── SereniteaPotViewModel.cs
│   │   │   └── TcgViewModel.cs
│   │   ├── OneDragonFlowViewModel.cs
│   │   ├── ScriptControlViewModel.cs
│   │   ├── TaskSettingsPageViewModel.cs
│   │   ├── TriggerSettingsPageViewModel.cs
│   │   └── View/
│   │   │   ├── AutoFightViewModel.cs
│   │   │   ├── PathingConfigViewModel.cs
│   │   │   └── ScriptGroupConfigViewModel.cs
│   └── Windows/
│   │   ├── AutoPickBlackListViewModel.cs
│   │   ├── AutoPickWhiteListViewModel.cs
│   │   ├── FormViewModel.cs
│   │   ├── JsonMonoViewModel.cs
│   │   └── MapViewerViewModel.cs
```
