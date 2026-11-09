@echo off
:: ============================================================
:: 生成 OshimaBot 的 Ubuntu linux-x64 框架依赖部署包 (fd)
::
:: 用法：在仓库根目录执行 publish-linux.bat（双击亦可）
::
:: 产物结构：
::   publish\linux-x64-fd\                可直接上传的部署目录
::     OshimaBot.dll + 全部依赖 dll + appsettings*.json
::     DEPLOY.txt                        英文部署说明：环境变量 / systemd / 备份
::   publish\OshimaBot-linux-x64-fd.zip  同内容压缩包
::
:: 目标机（Ubuntu 22.04 / 24.04 x64）需自行安装 .NET 10 ASP.NET Core Runtime，
:: 本包不含 .NET runtime，也不含本机 SQLite 数据库与任何凭据：
::   dotnet OshimaBot.dll --urls http://127.0.0.1:5090
::
:: 注意：本脚本用 goto 标签做错误处理（cmd 解析带转义括号的多行
::       if/else 分支块会失效，两分支都执行），不要改成多行 if/else。
:: ============================================================
setlocal
chcp 936 >nul 2>nul
cd /d "%~dp0"

set "OUT=publish\linux-x64-fd"
set "ZIP=publish\OshimaBot-linux-x64-fd.zip"
set "DEPLOY=%OUT%\DEPLOY.txt"

echo ==^> 1/6 检查构建环境
where dotnet >nul 2>nul
if errorlevel 1 goto err_no_dotnet
if not exist "OshimaBot\OshimaBot.csproj" goto err_no_project
if not exist "..\..\FunGame\FunGame.Core\src\FunGame.Core.csproj" goto err_no_core
dotnet --version

echo ==^> 2/6 清理旧发布产物
if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%ZIP%" del /f /q "%ZIP%"
if not exist publish mkdir publish

echo ==^> 3/6 发布 linux-x64 框架依赖版 --self-contained false
dotnet publish OshimaBot\OshimaBot.csproj -c Release -r linux-x64 --self-contained false -o "%OUT%" --no-restore
if not errorlevel 1 goto step4
echo       --no-restore 失败，清理半成品后回退到带还原的发布
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish OshimaBot\OshimaBot.csproj -c Release -r linux-x64 --self-contained false -o "%OUT%"
if errorlevel 1 goto err_publish

:step4
echo ==^> 4/6 校验产物、剔除本机数据并写入部署说明
if not exist "%OUT%\OshimaBot.dll" goto err_missing
if not exist "%OUT%\appsettings.json" goto err_missing
:: 本机数据库含账号、ApiKey 哈希与玩法数据，绝不随包发布；目标机首次启动自动建库
del /f /q "%OUT%\*.db*" >nul 2>nul
del /f /q "%OUT%\appsettings.*.local.json" >nul 2>nul
del /f /q "%OUT%\*.pfx" >nul 2>nul
del /f /q "%OUT%\rounds_archive.zip" >nul 2>nul

>"%DEPLOY%" echo OshimaBot - Ubuntu linux-x64 deployment package
>>"%DEPLOY%" echo ============================================================
>>"%DEPLOY%" echo Framework-dependent build. The .NET runtime is NOT included.
>>"%DEPLOY%" echo This package contains no local database and no credentials.
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo Prerequisites on the target host
>>"%DEPLOY%" echo   Ubuntu 22.04 or 24.04, x64
>>"%DEPLOY%" echo   .NET 10 ASP.NET Core Runtime already installed
>>"%DEPLOY%" echo   A writable directory for the SQLite database
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo 1. Extract
>>"%DEPLOY%" echo   sudo mkdir -p /opt/oshimobot
>>"%DEPLOY%" echo   sudo unzip -o OshimaBot-linux-x64-fd.zip -d /opt/oshimobot
>>"%DEPLOY%" echo   cd /opt/oshimobot
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo 2. Configure secrets with environment variables or an
>>"%DEPLOY%" echo    appsettings.Production.json override file.
>>"%DEPLOY%" echo    Never put real credentials into appsettings.json.
>>"%DEPLOY%" echo   ASPNETCORE_ENVIRONMENT=Production
>>"%DEPLOY%" echo   QQBot__Enabled=true
>>"%DEPLOY%" echo   QQBot__Mode=WebSocket
>>"%DEPLOY%" echo   QQBot__Environment=Production
>>"%DEPLOY%" echo   QQBot__AppId=YOUR_APP_ID
>>"%DEPLOY%" echo   QQBot__Secret=YOUR_BOT_SECRET
>>"%DEPLOY%" echo   Server__ApiKey=YOUR_THIRD_PARTY_KEY
>>"%DEPLOY%" echo   Database__ConnectionString=Data Source=oshimobot.db
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo   Notes
>>"%DEPLOY%" echo   - QQBot__Environment selects the official or sandbox API domain.
>>"%DEPLOY%" echo   - GatewayUrl left blank uses GET /gateway/bot auto discovery.
>>"%DEPLOY%" echo   - Server__ApiKey is a one-time bootstrap key. After the first
>>"%DEPLOY%" echo     start remove the plain value; auth then reads api_keys only.
>>"%DEPLOY%" echo   - Webhook is disabled by default. POST /QQBot returns 404.
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo 3. Start manually
>>"%DEPLOY%" echo   dotnet OshimaBot.dll --urls http://127.0.0.1:5090
>>"%DEPLOY%" echo   curl http://127.0.0.1:5090/health
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo 4. systemd unit at /etc/systemd/system/oshimobot.service
>>"%DEPLOY%" echo   [Unit]
>>"%DEPLOY%" echo   Description=OshimaBot QQ MUD
>>"%DEPLOY%" echo   After=network.target
>>"%DEPLOY%" echo   [Service]
>>"%DEPLOY%" echo   Type=simple
>>"%DEPLOY%" echo   WorkingDirectory=/opt/oshimobot
>>"%DEPLOY%" echo   ExecStart=/usr/bin/dotnet /opt/oshimobot/OshimaBot.dll --urls http://127.0.0.1:5090
>>"%DEPLOY%" echo   Environment=ASPNETCORE_ENVIRONMENT=Production
>>"%DEPLOY%" echo   Environment=QQBot__Enabled=true
>>"%DEPLOY%" echo   Environment=QQBot__AppId=YOUR_APP_ID
>>"%DEPLOY%" echo   Environment=QQBot__Secret=YOUR_BOT_SECRET
>>"%DEPLOY%" echo   Restart=always
>>"%DEPLOY%" echo   RestartSec=5
>>"%DEPLOY%" echo   [Install]
>>"%DEPLOY%" echo   WantedBy=multi-user.target
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo   sudo systemctl daemon-reload
>>"%DEPLOY%" echo   sudo systemctl enable --now oshimobot
>>"%DEPLOY%" echo   sudo journalctl -u oshimobot -f
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo 5. Data
>>"%DEPLOY%" echo   The database is created and migrated on first start. Relative
>>"%DEPLOY%" echo   paths in Database__ConnectionString resolve against
>>"%DEPLOY%" echo   WorkingDirectory. Stop the service before backup, then copy
>>"%DEPLOY%" echo   oshimobot.db, oshimobot.db-wal and oshimobot.db-shm together,
>>"%DEPLOY%" echo   or use VACUUM INTO for a consistent snapshot.
>>"%DEPLOY%" echo   Simulation statistics and logs live in simulation_statistics
>>"%DEPLOY%" echo   and simulation_runs. No JSON save files are needed.
>>"%DEPLOY%" echo.
>>"%DEPLOY%" echo 6. Optional reverse proxy
>>"%DEPLOY%" echo   nginx proxy_pass to http://127.0.0.1:5090
>>"%DEPLOY%" echo   GET /health is the readiness endpoint.
if not exist "%DEPLOY%" goto err_copy

echo ==^> 5/6 打包 zip
if not exist "%SystemRoot%\System32\tar.exe" goto no_tar
pushd "%OUT%"
"%SystemRoot%\System32\tar.exe" -a -cf "..\OshimaBot-linux-x64-fd.zip" *
if errorlevel 1 goto err_zip_in
popd
goto step6

:err_zip_in
popd
goto err_zip

:no_tar
echo       未找到系统 tar.exe，跳过压缩包，仅保留目录产物

:step6
echo ==^> 6/6 完成
echo.
echo 部署目录 : %CD%\%OUT%
if exist "%ZIP%" echo 压缩包   : %CD%\%ZIP%
echo.
echo 目标机需预装 .NET 10 ASP.NET Core Runtime，上传解压后执行：
echo   dotnet OshimaBot.dll --urls http://127.0.0.1:5090
echo   curl http://127.0.0.1:5090/health
echo.
echo 凭据用环境变量注入（QQBot__AppId / QQBot__Secret / Server__ApiKey 等），
echo 不要写进 appsettings.json；数据库首次启动自动创建。详见包内 DEPLOY.txt。
echo.
goto :eof

:err_no_dotnet
echo [错误] 未找到 dotnet，请先安装 .NET 10 SDK
exit /b 1

:err_no_project
echo [错误] 请在仓库根目录运行本脚本（找不到 OshimaBot\OshimaBot.csproj）
exit /b 1

:err_no_core
echo [错误] 找不到仓库外的 FunGame.Core.csproj
echo        OshimaBot.slnx 相对引用 ..\..\FunGame\FunGame.Core\src\FunGame.Core.csproj
echo        换机器编译时需先把 FunGame.Core 放到对应位置，或改成子模块 / 包引用
exit /b 1

:err_publish
echo [错误] dotnet publish 失败
exit /b 1

:err_missing
echo [错误] 发布产物缺少 OshimaBot.dll 或 appsettings.json
exit /b 1

:err_copy
echo [错误] 写入 DEPLOY.txt 失败
exit /b 1

:err_zip
echo [错误] 打包 zip 失败，publish\linux-x64-fd 目录产物仍可使用
exit /b 1

endlocal
