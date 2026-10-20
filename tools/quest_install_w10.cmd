@echo off
for /d %%D in ("C:\Program Files\Unity\Hub\Editor\*") do set ADB=%%D\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
cd /d C:\TacticalMedicine
"%ADB%" install -r -g "C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk" > logs\device\w10_install.log 2>&1
"%ADB%" logcat -c
"%ADB%" shell monkey -p kz.qorsa.fieldmedic -c android.intent.category.LAUNCHER 1 >> logs\device\w10_install.log 2>&1
timeout /t 25 /nobreak > nul
"%ADB%" pull /sdcard/Android/data/kz.qorsa.fieldmedic/files/fmvr_log.txt logs\device\w10_fmvr_log.txt >> logs\device\w10_install.log 2>&1
echo DONE >> logs\device\w10_install.log
