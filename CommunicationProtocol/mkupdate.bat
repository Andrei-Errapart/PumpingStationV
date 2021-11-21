REM Update the protocol definitions.

cd ..\PlcMaster
set COMMDIR=..\CommunicationProtocol
%COMMDIR%\protoc.exe --java_out=src --proto_path=%COMMDIR% %COMMDIR%\PlcCommunication.proto

cd ..\ControlPanel
%COMMDIR%\protogen.exe -namespace=ControlPanel --proto_path=%COMMDIR%  %COMMDIR%\PlcCommunication.proto

