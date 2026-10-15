Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "WinVer.nsh"
Name "AlterDPI"
Caption "AlterDPI · Kurulum"
UninstallCaption "AlterDPI · Kaldırma"
BrandingText "AlterDPI · Furkan Okan"
OutFile "..\artifacts\release\AlterDPI-Kurulum.exe"
InstallDir "$PROGRAMFILES64\AlterDPI"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "0.5.0.0"
VIAddVersionKey /LANG=1055 "ProductName" "AlterDPI"
VIAddVersionKey /LANG=1055 "FileDescription" "AlterDPI Kurulum"
VIAddVersionKey /LANG=1055 "FileVersion" "0.5.0fx"
VIAddVersionKey /LANG=1055 "LegalCopyright" "Furkan Okan"
!define MUI_ICON "..\assets\alterdpi.ico"
!define MUI_UNICON "..\assets\alterdpi.ico"
!define MUI_ABORTWARNING
!define MUI_BGCOLOR "10151D"
!define MUI_TEXTCOLOR "F0F4FA"
!define MUI_INSTFILESPAGE_COLORS "F0F4FA 1A222E"
!define MUI_WELCOMEFINISHPAGE_BITMAP "..\assets\setup-sidebar.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "..\assets\setup-sidebar.bmp"
!define MUI_WELCOMEPAGE_TITLE "AlterDPI 0.5.0fx"
!define MUI_WELCOMEPAGE_TEXT "AlterDPI bağlantı yöntemlerini ağınıza göre düzenler.$\r$\n$\r$\nİlk kurulumda erişim, web koruması ve Windows başlangıcı kapalıdır.$\r$\n$\r$\nGerekli çalışma zamanı pakete dahildir; ek indirme gerekmez."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchUser
!define MUI_FINISHPAGE_RUN_TEXT "AlterDPI'ı aç"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.Options un.OptionsLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Turkish"
Var Backup
Var Result
Var DeleteLogs
Var LogBox
!macro CleanEnvironment
 System::Call 'kernel32::SetEnvironmentVariableW(w "DOTNET_STARTUP_HOOKS", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "DOTNET_ADDITIONAL_DEPS", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "DOTNET_SHARED_STORE", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "CORECLR_ENABLE_PROFILING", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "CORECLR_PROFILER", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "CORECLR_PROFILER_PATH", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "CORECLR_PROFILER_PATH_64", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "COR_ENABLE_PROFILING", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "COR_PROFILER", p 0)'
 System::Call 'kernel32::SetEnvironmentVariableW(w "COR_PROFILER_PATH", p 0)'
!macroend
Function .onInit
 !insertmacro CleanEnvironment
 ${IfNot} ${RunningX64}
  MessageBox MB_ICONSTOP "Bu paket Windows x64 içindir."
  Abort
 ${EndIf}
 ${IfNot} ${AtLeastWin10}
  MessageBox MB_ICONSTOP "Windows 10 veya 11 x64 gerekir."
  Abort
 ${EndIf}
 System::Alloc 64
 Pop $0
 System::Call 'kernel32::GetNativeSystemInfo(p r0)'
 System::Call '*$0(&i2 .r1)'
 System::Free $0
 ${If} $1 != 9
  MessageBox MB_ICONSTOP "Bu pilot paket yalnız x64 işlemciler içindir."
  Abort
 ${EndIf}
 StrCpy $INSTDIR "$PROGRAMFILES64\AlterDPI"
 IfFileExists "$INSTDIR\*.*" 0 done
 IfFileExists "$INSTDIR\install.owner" done
 MessageBox MB_ICONSTOP "Hedef klasör bu uygulamaya ait değil. Kurulum iptal edildi."
 Abort
 done:
FunctionEnd
Function LaunchUser
 Exec '"$WINDIR\explorer.exe" "$INSTDIR\AlterDPI.exe"'
FunctionEnd
Section "AlterDPI"
 SetRegView 64
 SetShellVarContext all
 StrCpy $Backup "$PROGRAMFILES64\AlterDPI.rollback"
 IfFileExists "$Backup\*.*" 0 continue
 MessageBox MB_ICONSTOP "Önceki geri dönüş klasörü bulundu. Dosyalar korunarak kurulum durduruldu."
 Abort
 continue:
 IfFileExists "$INSTDIR\AlterDPI.exe" 0 copyfiles
 ExecWait '"$INSTDIR\AlterDPI.exe" --prepare-upgrade' $Result
 ${If} $Result != 0
  MessageBox MB_ICONSTOP "Mevcut kurulum güvenli biçimde durdurulamadı. Dosyalar değiştirilmedi."
  Abort
 ${EndIf}
 ClearErrors
 Rename "$INSTDIR" "$Backup"
 ${If} ${Errors}
  ExecWait '"$INSTDIR\AlterDPI.exe" --restore' $Result
  MessageBox MB_ICONSTOP "Uygulama dosyaları taşınamadı. Önceki kurulum geri hazırlanmaya çalışıldı; AlterDPI'ı kapatıp tekrar deneyin."
  Abort
 ${EndIf}
 copyfiles:
 SetOutPath "$INSTDIR"
 File /r "..\artifacts\payload\*.*"
 WriteUninstaller "$INSTDIR\Kaldir.exe"
 ExecWait '"$INSTDIR\AlterDPI.exe" --configure' $Result
 ${If} $Result != 0
  ExecWait '"$INSTDIR\AlterDPI.exe" --unconfigure'
  RMDir /r "$INSTDIR"
  IfFileExists "$Backup\*.*" 0 failed
  Rename "$Backup" "$INSTDIR"
  ExecWait '"$INSTDIR\AlterDPI.exe" --restore'
  failed:
  MessageBox MB_ICONSTOP "Kurulum tamamlanamadı. Önceki sürüm varsa geri getirildi."
  Abort
 ${EndIf}
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AlterDPI" "DisplayName" "AlterDPI"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AlterDPI" "DisplayVersion" "0.5.0fx"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AlterDPI" "Publisher" "Furkan Okan"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AlterDPI" "DisplayIcon" "$INSTDIR\AlterDPI.exe"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AlterDPI" "UninstallString" '"$INSTDIR\Kaldir.exe"'
 CreateDirectory "$SMPROGRAMS\AlterDPI"
 CreateShortcut "$SMPROGRAMS\AlterDPI\AlterDPI.lnk" "$INSTDIR\AlterDPI.exe"
 IfFileExists "$Backup\install.owner" 0 end
 ExecWait '"$INSTDIR\AlterDPI.exe" --validate-backup' $Result
 ${If} $Result == 0
  RMDir /r /REBOOTOK "$Backup"
 ${EndIf}
 end:
SectionEnd
Function un.Options
 nsDialogs::Create 1018
 Pop $0
 ${If} $0 == error
  Abort
 ${EndIf}
 ${NSD_CreateLabel} 0 0 100% 38u "AlterDPI'ın hizmeti, başlangıç kaydı, tarayıcı bağlantısı ve kendi DNS kuralları kaldırılır."
 Pop $0
 ${NSD_CreateCheckbox} 0 48u 100% 28u "Hata kayıtlarını da sil"
 Pop $LogBox
 nsDialogs::Show
FunctionEnd
Function un.OptionsLeave
 ${NSD_GetState} $LogBox $DeleteLogs
FunctionEnd
Function un.onInit
 !insertmacro CleanEnvironment
FunctionEnd
Section "Uninstall"
 SetRegView 64
 SetShellVarContext all
 StrCpy $INSTDIR "$PROGRAMFILES64\AlterDPI"
 IfFileExists "$INSTDIR\install.owner" 0 refused
 ExecWait '"$INSTDIR\AlterDPI.exe" --unconfigure' $Result
 ${If} $Result != 0
  MessageBox MB_ICONSTOP "Ağ ayarları geri yüklenemedi. Dosyalar korundu.$\r$\n$\r$\nHata kayıtları: $LOCALAPPDATA\AlterDPI\kayitlar$\r$\nİşlem kodu: $Result"
  Abort
 ${EndIf}
 Delete "$SMPROGRAMS\AlterDPI\AlterDPI.lnk"
 RMDir "$SMPROGRAMS\AlterDPI"
 DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AlterDPI"
 ${If} $DeleteLogs == ${BST_CHECKED}
  ExecWait '"$INSTDIR\AlterDPI.exe" --delete-logs' $Result
  ${If} $Result != 0
   MessageBox MB_ICONEXCLAMATION "Yerel kayıtların sahipliği doğrulanamadı veya dosyalar kullanımda. Kayıtlar korundu."
  ${EndIf}
 ${Else}
  ExecWait '"$INSTDIR\AlterDPI.exe" --keep-logs' $Result
 ${EndIf}
 IfFileExists "$PROGRAMFILES64\AlterDPI.rollback\install.owner" 0 backup_done
 ExecWait '"$INSTDIR\AlterDPI.exe" --validate-backup' $Result
 ${If} $Result == 0
  RMDir /r /REBOOTOK "$PROGRAMFILES64\AlterDPI.rollback"
 ${EndIf}
 backup_done:
 RMDir /r /REBOOTOK "$INSTDIR"
 Goto end
 refused:
 MessageBox MB_ICONSTOP "Kurulum sahipliği doğrulanamadı. Dosyalar korunuyor."
 Abort
 end:
SectionEnd
