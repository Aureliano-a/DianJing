Unicode true
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinMessages.nsh"
!ifdef TEST_BUILD
  !define APPKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DianJing-Test"
  OutFile "${__FILEDIR__}\..\..\DianJing-Setup-Test.exe"
!else
  !define APPKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DianJing"
  OutFile "${__FILEDIR__}\..\..\DianJing-Setup-v12.exe"
!endif
Name "点境"
Caption "$(AppTitle)"
BrandingText "点境  /  DIANJING"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\DianJing"
InstallDirRegKey HKCU "${APPKEY}" "InstallLocation"
SetCompressor /SOLID lzma
SetCompressorDictSize 32
Icon "${__FILEDIR__}\..\src\NexusClick.App\Assets\dianjing.ico"
UninstallIcon "${__FILEDIR__}\..\src\NexusClick.App\Assets\dianjing.ico"
VIProductVersion "1.0.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "点境"
VIAddVersionKey /LANG=2052 "FileDescription" "点境安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "1.0.0.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright 2026 点境"
!define MUI_LANGDLL_ALLLANGUAGES
!define MUI_ABORTWARNING
!define MUI_ICON "${__FILEDIR__}\..\src\NexusClick.App\Assets\dianjing.ico"
!define MUI_UNICON "${__FILEDIR__}\..\src\NexusClick.App\Assets\dianjing.ico"
!define MUI_FINISHPAGE_TITLE "$(DoneTitle)"
!define MUI_FINISHPAGE_TEXT "$(DoneText)"
!define MUI_FINISHPAGE_RUN "$INSTDIR\DianJing.exe"
!define MUI_FINISHPAGE_RUN_TEXT "$(RunText)"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION OpenInstallFolder
!define MUI_FINISHPAGE_SHOWREADME_TEXT "$(FolderText)"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
Page custom ConfigPage ConfigLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Japanese"
!insertmacro MUI_LANGUAGE "Spanish"
!insertmacro MUI_LANGUAGE "Russian"
!include "${__FILEDIR__}\Translations.nsh"

Var Dialog
Var PathBox
Var DesktopBox
Var DesktopChoice
Var LogoHandle
Var LogoControl
Var TitleFont
Var SmallFont
Var DesktopOwned
Var StartOwned
Var ApprovedOverwrite
Var RequestedDir

Function un.onInit
  ReadRegStr $0 HKCU "${APPKEY}" "InstallerLanguage"
  ${If} $0 != ""
    StrCpy $LANGUAGE $0
  ${EndIf}
FunctionEnd

Function OpenInstallFolder
  ExecShell "open" "$INSTDIR"
FunctionEnd

Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
  StrCpy $DesktopChoice ${BST_CHECKED}
  StrCpy $RequestedDir $INSTDIR
  InitPluginsDir
  File /oname=$PLUGINSDIR\brand.bmp "${__FILEDIR__}\brand.bmp"
  CreateFont $TitleFont "Microsoft YaHei UI" 18 700
  CreateFont $SmallFont "Microsoft YaHei UI" 9 400
FunctionEnd

Function HideHeader
  GetDlgItem $0 $HWNDPARENT 1028
  ShowWindow $0 ${SW_HIDE}
  GetDlgItem $0 $HWNDPARENT 1035
  ShowWindow $0 ${SW_HIDE}
  GetDlgItem $0 $HWNDPARENT 1037
  ShowWindow $0 ${SW_HIDE}
  GetDlgItem $0 $HWNDPARENT 1038
  ShowWindow $0 ${SW_HIDE}
  GetDlgItem $0 $HWNDPARENT 1039
  ShowWindow $0 ${SW_HIDE}
FunctionEnd
Function ShowHeader
  GetDlgItem $0 $HWNDPARENT 1028
  ShowWindow $0 ${SW_SHOW}
  GetDlgItem $0 $HWNDPARENT 1035
  ShowWindow $0 ${SW_SHOW}
  GetDlgItem $0 $HWNDPARENT 1037
  ShowWindow $0 ${SW_SHOW}
  GetDlgItem $0 $HWNDPARENT 1038
  ShowWindow $0 ${SW_SHOW}
  GetDlgItem $0 $HWNDPARENT 1039
  ShowWindow $0 ${SW_SHOW}
FunctionEnd

Function ConfigPage
  nsDialogs::Create 1044
  Pop $Dialog
  ${If} $Dialog == error
    Abort
  ${EndIf}
  SetCtlColors $Dialog 0xDDEEFF 0x101320
  ${NSD_CreateBitmap} 5u 5u 92u 92u ""
  Pop $LogoControl
  ${NSD_SetImage} $LogoControl "$PLUGINSDIR\brand.bmp" $LogoHandle
  ${NSD_CreateLabel} 109u 10u 195u 30u "点境"
  Pop $0
  SendMessage $0 ${WM_SETFONT} $TitleFont 1
  SetCtlColors $0 0xF5FAFF 0x101320
  ${NSD_CreateLabel} 110u 45u 190u 15u "DIANJING  /  DEEP SPACE"
  Pop $0
  SendMessage $0 ${WM_SETFONT} $SmallFont 1
  SetCtlColors $0 0xFFD9A2 0x101320
  ${NSD_CreateLabel} 110u 64u 190u 27u "$(DescText)"
  Pop $0
  SendMessage $0 ${WM_SETFONT} $SmallFont 1
  SetCtlColors $0 0xEADCCC 0x101320
  ${NSD_CreateLabel} 9u 105u 280u 15u "$(PathText)"
  Pop $0
  SetCtlColors $0 0xF5EADF 0x101320
  ${NSD_CreateText} 9u 123u 227u 15u "$INSTDIR"
  Pop $PathBox
  SetCtlColors $PathBox 0xF5FAFF 0x302218
  ${NSD_CreateBrowseButton} 241u 123u 62u 15u "$(BrowseText)"
  Pop $0
  ${NSD_OnClick} $0 BrowseDir
  ${NSD_CreateCheckbox} 9u 148u 14u 14u ""
  Pop $DesktopBox
  SetCtlColors $DesktopBox 0xEAF5FF 0x101320
  ${NSD_SetState} $DesktopBox $DesktopChoice
  ${NSD_CreateLabel} 25u 148u 240u 14u "$(ShortcutText)"
  Pop $0
  SetCtlColors $0 0xF5F0E6 0x101320
  ${NSD_CreateLabel} 9u 169u 290u 13u "$(FootText)"
  Pop $0
  SetCtlColors $0 0xB6B3A2 0x101320
  Call HideHeader
  nsDialogs::Show
  Call ShowHeader
  ${NSD_FreeImage} $LogoHandle
FunctionEnd
Function BrowseDir
  ${NSD_GetText} $PathBox $0
  nsDialogs::SelectFolderDialog "$(PickText)" $0
  Pop $0
  ${If} $0 != error
    StrCpy $1 $0 1 -1
    ${If} $1 == "\"
      StrCpy $0 $0 -1
    ${EndIf}
    ${GetFileName} "$0" $1
    ${If} $1 != "DianJing"
      StrCpy $0 "$0\DianJing"
    ${EndIf}
    ${NSD_SetText} $PathBox $0
  ${EndIf}
FunctionEnd
Function ConfigLeave
  ${NSD_GetText} $PathBox $RequestedDir
  ${NSD_GetState} $DesktopBox $DesktopChoice
  Call ValidateInstallDir
FunctionEnd

Function ValidateInstallDir
  ${If} $RequestedDir == ""
    IfSilent 0 +2
    SetErrorLevel 3
    IfSilent +2 0
    MessageBox MB_ICONEXCLAMATION "$(EmptyText)"
    Abort
  ${EndIf}
  ; NSIS may strip the final slash from a drive root when storing $INSTDIR.
  StrCpy $0 $RequestedDir
  StrLen $1 $0
  StrCpy $2 $0 1 1
  ${If} $1 == 2
  ${AndIf} $2 == ":"
    StrCpy $0 "$0\"
  ${EndIf}
  ; GetFullPathNameW resolves paths even if their final folder does not yet exist.
  System::Call 'kernel32::GetFullPathNameW(w r0, i ${NSIS_MAX_STRLEN}, w .r1, p 0) i.r2'
  ${If} $2 <= 0
  ${OrIf} $2 >= ${NSIS_MAX_STRLEN}
    IfSilent 0 +2
    SetErrorLevel 3
    IfSilent +2 0
    MessageBox MB_ICONEXCLAMATION "$(BadPathText)"
    Abort
  ${EndIf}
  ${GetRoot} "$1" $3
  ${If} $3 == ""
    IfSilent 0 +2
    SetErrorLevel 3
    IfSilent +2 0
    MessageBox MB_ICONEXCLAMATION "$(BadRootText)"
    Abort
  ${EndIf}
  StrCpy $4 $1 1 -1
  ${If} $4 == "\"
    StrCpy $4 $1 -1
  ${Else}
    StrCpy $4 $1
  ${EndIf}
  ${If} $4 == $3
    StrCpy $1 "$3\DianJing"
  ${EndIf}
  StrCpy $INSTDIR $1
  ReadRegStr $0 HKCU "${APPKEY}" "InstallLocation"
  ${If} $0 != ""
  ${AndIf} $0 != $INSTDIR
    IfSilent 0 +2
    SetErrorLevel 3
    IfSilent +2 0
    MessageBox MB_ICONEXCLAMATION "$(OtherText)"
    Abort
  ${EndIf}
  ${If} $0 == ""
  ${AndIf} $ApprovedOverwrite != 1
    IfFileExists "$INSTDIR\DianJing.exe" conflict 0
    IfFileExists "$INSTDIR\Uninstall-DianJing.exe" conflict 0
    IfFileExists "$INSTDIR\DianJing-使用说明.md" conflict validated
conflict:
    IfSilent 0 +3
    SetErrorLevel 3
    Abort
    MessageBox MB_YESNO|MB_ICONQUESTION "$(OverwriteText)" IDYES +2
    Abort
    StrCpy $ApprovedOverwrite 1
validated:
  ${EndIf}
FunctionEnd

Section "Install"
  Call ValidateInstallDir
  SetShellVarContext current
  ClearErrors
  SetOutPath "$INSTDIR"
  IfErrors install_error
  File /oname=DianJing.exe "${__FILEDIR__}\..\..\DianJing-Portable-v7\DianJing.exe"
  IfErrors install_error
  File /oname=DianJing-使用说明.md "${__FILEDIR__}\..\..\DianJing-Portable-v7\使用说明.md"
  IfErrors install_error
  WriteUninstaller "$INSTDIR\Uninstall-DianJing.exe"
  IfErrors install_error
  ReadRegDWORD $DesktopOwned HKCU "${APPKEY}" "DesktopOwned"
  ReadRegDWORD $StartOwned HKCU "${APPKEY}" "StartOwned"
!ifndef TEST_BUILD
  ${If} $DesktopChoice == ${BST_CHECKED}
    IfFileExists "$DESKTOP\点境.lnk" +3 0
    CreateShortCut "$DESKTOP\点境.lnk" "$INSTDIR\DianJing.exe" "" "$INSTDIR\DianJing.exe" 0
    StrCpy $DesktopOwned 1
  ${EndIf}
  IfFileExists "$SMPROGRAMS\点境.lnk" +3 0
  CreateShortCut "$SMPROGRAMS\点境.lnk" "$INSTDIR\DianJing.exe" "" "$INSTDIR\DianJing.exe" 0
  StrCpy $StartOwned 1
!endif
  WriteRegStr HKCU "${APPKEY}" "InstallerLanguage" "$LANGUAGE"
  WriteRegStr HKCU "${APPKEY}" "DisplayName" "点境"
  WriteRegStr HKCU "${APPKEY}" "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "${APPKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${APPKEY}" "DisplayIcon" "$INSTDIR\DianJing.exe"
  WriteRegStr HKCU "${APPKEY}" "UninstallString" '"$INSTDIR\Uninstall-DianJing.exe"'
  WriteRegDWORD HKCU "${APPKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APPKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${APPKEY}" "DesktopOwned" $DesktopOwned
  WriteRegDWORD HKCU "${APPKEY}" "StartOwned" $StartOwned
!ifndef TEST_BUILD
  ; A first installation adopts the installer language; preserve existing settings on update.
  IfFileExists "$LOCALAPPDATA\DianJing\language.txt" language_saved
  StrCpy $0 0
  ${If} $LANGUAGE == ${LANG_ENGLISH}
    StrCpy $0 1
  ${ElseIf} $LANGUAGE == ${LANG_JAPANESE}
    StrCpy $0 2
  ${ElseIf} $LANGUAGE == ${LANG_SPANISH}
    StrCpy $0 3
  ${ElseIf} $LANGUAGE == ${LANG_RUSSIAN}
    StrCpy $0 4
  ${EndIf}
  CreateDirectory "$LOCALAPPDATA\DianJing"
  ClearErrors
  FileOpen $1 "$LOCALAPPDATA\DianJing\language.txt" w
  IfErrors language_saved
  FileWrite $1 "$0"
  FileClose $1
language_saved:
!endif
  Goto install_done
install_error:
  SetErrorLevel 3
  IfSilent +2 0
  MessageBox MB_ICONEXCLAMATION "$(WriteFailText)"
  Abort
install_done:
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  ReadRegDWORD $DesktopOwned HKCU "${APPKEY}" "DesktopOwned"
  ReadRegDWORD $StartOwned HKCU "${APPKEY}" "StartOwned"
  IfFileExists "$INSTDIR\DianJing.exe" 0 app_removed
retry_remove_app:
  ClearErrors
  Delete "$INSTDIR\DianJing.exe"
  IfErrors 0 app_removed
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(DeleteFailText)" IDRETRY retry_remove_app
  Abort
app_removed:
!ifndef TEST_BUILD
  ${If} $DesktopOwned == 1
    Delete "$DESKTOP\点境.lnk"
  ${EndIf}
  ${If} $StartOwned == 1
    Delete "$SMPROGRAMS\点境.lnk"
  ${EndIf}
!endif
  Delete "$INSTDIR\DianJing-使用说明.md"
  Delete "$INSTDIR\Uninstall-DianJing.exe"
  DeleteRegKey HKCU "${APPKEY}"
  RMDir "$INSTDIR"
SectionEnd




