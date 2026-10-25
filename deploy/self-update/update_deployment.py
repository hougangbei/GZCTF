"""Native and Compose adapters; commands and deployment paths come only from host config."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import socket
import subprocess
import tarfile
import time
import tempfile
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

from update_sources import SourceError, tree_digest, verify_release


def run(args, *, cwd=None, env=None, timeout=120, input=None):
    result=subprocess.run([str(arg) for arg in args],cwd=cwd,env=env,input=input,
        stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=timeout)
    if result.returncode:
        # Command output stays in a private host log, not the administrator API.
        import logging
        logging.getLogger('gzctf-updater').error('Command failed: %s\n%s',args[0],(result.stdout+result.stderr).decode(errors='replace')[-6000:])
        raise SourceError(f'{Path(str(args[0])).name} 执行失败，请查看宿主机更新日志。')
    return result.stdout


def runtime_id():
    system={'Darwin':'osx','Linux':'linux'}.get(platform.system())
    arch={'arm64':'arm64','aarch64':'arm64','x86_64':'x64','AMD64':'x64'}.get(platform.machine())
    if not system or not arch: raise SourceError('当前系统不支持本机更新。')
    return f'{system}-{arch}'


def validate_database_identity(name,user):
    if name in {'postgres','template0','template1'} or not re.fullmatch(r'[a-zA-Z_][a-zA-Z0-9_]{0,39}',name):
        raise ValueError('Unsupported application database name')
    if not re.fullmatch(r'[a-zA-Z_][a-zA-Z0-9_]{0,39}',user): raise ValueError('Unsupported database user')


def connection_string(host,port,database,user,password):
    def quote(value): return '"'+str(value).replace('"','""')+'"'
    return ';'.join(f'{key}={quote(value)}' for key,value in
        [('Host',host),('Port',port),('Database',database),('Username',user),('Password',password)])


def writable_directory(path):
    path=Path(path)
    temporary=None; renamed=None
    try:
        with tempfile.NamedTemporaryFile(prefix='.gzctf-write-',dir=path,delete=False) as output:
            temporary=Path(output.name); output.write(b'probe')
        renamed=temporary.with_suffix('.renamed')
        os.replace(temporary,renamed)
    except OSError as error:
        raise SourceError('部署、备份或恢复目录不可写，不能安全替换。') from error
    finally:
        if temporary: temporary.unlink(missing_ok=True)
        if renamed: renamed.unlink(missing_ok=True)


def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1',0)); return sock.getsockname()[1]


def await_health(url, *, process=None, timeout=120):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        if process is not None and process.poll() is not None: raise SourceError('候选程序提前退出，无法替换当前平台。')
        try:
            with urllib.request.urlopen(url,timeout=3) as response:
                if response.status==200 and response.read(100)==b'Healthy': return
        except (OSError,urllib.error.URLError): pass
        time.sleep(1)
    raise SourceError('候选版本未通过健康检查，请检查运行环境或版本兼容性。')


class Database:
    def __init__(self, config):
        self.config=config; self.name=config['name']; self.user=config['user']; self.container=config['container']
        validate_database_identity(self.name,self.user)
        if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]*',self.container): raise ValueError('Invalid database container')
    def command(self,*args,input=None,timeout=120):
        return run(['docker','exec','-i',self.container,*args],input=input,timeout=timeout)
    def dump(self,path):
        with path.open('xb') as output:
            proc=subprocess.run(['docker','exec',self.container,'pg_dump','-U',self.user,'-d',self.name,'-Fc'],
                stdout=output,stderr=subprocess.PIPE,timeout=600)
        os.chmod(path,0o600)
        if proc.returncode or path.stat().st_size==0:
            import logging
            logging.getLogger('gzctf-updater').error('Database dump failed (%s): %s',proc.returncode,proc.stderr.decode(errors='replace')[-4000:])
            raise SourceError('数据库备份失败，已取消更新。')
    def restore(self,path,name):
        validate_database_identity(name,self.user)
        self.command('dropdb','-U',self.user,'--if-exists','--force',name)
        self.command('createdb','-U',self.user,'-O',self.user,name)
        with path.open('rb') as source:
            proc=subprocess.run(['docker','exec','-i',self.container,'pg_restore','-U',self.user,'-d',name,
                '--no-owner','--exit-on-error'],stdin=source,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=600)
        if proc.returncode: raise SourceError('数据库恢复失败，请使用备份由运维恢复。')
    def drop(self,name):
        if not name.startswith('gzctf_check_'): raise ValueError('Only disposable validation databases can be cleaned')
        self.command('dropdb','-U',self.user,'--if-exists','--force',name)
    def migrations(self,name=None):
        name=name or self.name
        exists=self.command('psql','-U',self.user,'-d',name,'-At','-c',
            "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL").decode().strip()
        if exists!='t': return []
        return self.command('psql','-U',self.user,'-d',name,'-At','-c',
            'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"').decode().splitlines()
    def size(self):
        return int(self.command('psql','-U',self.user,'-d',self.name,'-At','-c',
            'SELECT pg_database_size(current_database())').decode().strip())
    def connection(self,name):
        # The password is read from a private host settings file, never returned by the agent.
        config=self.config
        return connection_string(config.get('host','127.0.0.1'),config.get('port',5432),name,self.user,config['password'])


class Deployment:
    def __init__(self, config):
        self.config=config
        self.mode=config['deployment_mode']
        self.runtime=config.get('runtime') or (runtime_id() if self.mode=='native' else 'linux-x64')
        self.work_dir=Path(config['work_dir']).resolve()
        self.files_dir=Path(config['files_dir']).resolve()
        self.database=Database(config['database'])
        self.tools=config.get('tools',{})
    def preflight(self):
        for tool in ('docker','dotnet','pnpm','node'):
            executable=self.tools.get(tool,tool)
            if not (Path(executable).is_file() if '/' in executable else shutil.which(executable)):
                raise SourceError(f'宿主机缺少 {tool}，无法构建或验证更新。')
        self.work_dir.mkdir(parents=True,exist_ok=True,mode=0o700)
        backup_dir=Path(self.config['backup_dir'])
        backup_dir.mkdir(parents=True,exist_ok=True,mode=0o700)
        for directory in (self.work_dir,backup_dir,self.files_dir.parent): writable_directory(directory)
        if not self.files_dir.is_dir() or self.files_dir.is_symlink(): raise SourceError('上传文件目录不存在或不是独立目录。')
        files_size=sum(p.stat().st_size for p in self.files_dir.rglob('*') if p.is_file())
        self.database.command('pg_isready','-U',self.database.user,'-d',self.database.name)
        database_size=self.database.size()
        work_required=max(2*1024**3,files_size*2+database_size*2)
        backup_required=max(256*1024**2,files_size+database_size*2)
        if self.work_dir.stat().st_dev==backup_dir.stat().st_dev: work_required+=backup_required
        if shutil.disk_usage(self.work_dir).free < work_required or shutil.disk_usage(backup_dir).free < backup_required:
            raise SourceError('暂存或备份磁盘空间不足，无法保留完整数据库及上传文件备份。')
        if shutil.disk_usage(self.files_dir.parent).free < files_size:
            raise SourceError('上传目录所在磁盘空间不足，无法准备恢复副本。')
        return '构建工具、数据库连接、目录权限和磁盘空间可用。'
    def build(self,source,artifact,revision):
        if (source/'gzctf-release.json').exists():
            manifest=verify_release(source,self.runtime)
            shutil.copytree(source,artifact)
            return str(manifest['version'])
        project=source/'src/GZCTF/GZCTF.csproj'
        try:
            xml=ET.parse(project).getroot()
            version=xml.findtext('.//ReleaseVersion') or xml.findtext('.//AssemblyVersion')
            framework=xml.findtext('.//TargetFramework')
        except (OSError,ET.ParseError) as error: raise SourceError('来源缺少有效的 GZCTF 项目。') from error
        if framework!='net10.0' or not version: raise SourceError('来源不是兼容的 .NET 10 GZCTF 项目。')
        program=(source/'src/GZCTF/Program.cs').read_text(encoding='utf-8-sig')
        if '/update-validation' not in program:
            raise SourceError('该源码版本尚不支持隔离更新校验，不能替换。请使用包含本更新功能的版本。')
        env=os.environ.copy()
        paths=[str(Path(value).parent) for value in self.tools.values() if '/' in value]
        env['PATH']=os.pathsep.join(paths+[env.get('PATH','')])
        env.update(CI='true',VITE_APP_GIT_SHA=revision or tree_digest(source),VITE_APP_GIT_NAME='validated-update')
        run([self.tools.get('pnpm','pnpm'),'install','--frozen-lockfile'],cwd=source/'src/GZCTF/ClientApp',env=env,timeout=600)
        run([self.tools.get('dotnet','dotnet'),'publish',str(project),'-c','Release','-o',str(artifact),
            '-r',self.runtime,'--no-self-contained','--nologo'],cwd=source/'src',env=env,timeout=1200)
        if not (artifact/'GZCTF.dll').is_file() or not (artifact/'wwwroot/index.html').is_file():
            raise SourceError('构建产物缺少后端或前端首页。')
        manifest={'project':'GZCTF','validationProtocol':1,'version':version,'runtime':self.runtime,'revision':revision,
            'files':{path.relative_to(artifact).as_posix():hashlib.sha256(path.read_bytes()).hexdigest()
                     for path in artifact.rglob('*') if path.is_file()}}
        (artifact/'gzctf-release.json').write_text(json.dumps(manifest,indent=2))
        verify_release(artifact,self.runtime)
        return version
    def migrations(self): return self.database.migrations()
    def validate(self,artifact,workspace):
        original=self.migrations()
        dump=workspace/'validation.dump'; self.database.dump(dump)
        name='gzctf_check_'+workspace.name[:16]
        try:
            self.database.restore(dump,name)
            self.smoke(artifact,workspace,name)
            applied=self.database.migrations(name)
            if not set(original).issubset(applied): raise SourceError('候选版本缺少当前数据库的迁移记录，不能替换。')
            return original,'数据库副本已完成迁移，候选程序与前端首页通过隔离检查。'
        finally:
            self.database.drop(name)
            dump.unlink(missing_ok=True)
    def backup(self,destination):
        destination.mkdir(parents=True,mode=0o700)
        self.database.dump(destination/'database.dump')
        shutil.copytree(self.files_dir,destination/'files',symlinks=False)
        metadata=self.deployment_metadata()
        return metadata
    def restore(self,backup,metadata):
        self.database.restore(backup/'database.dump',self.database.name)
        # Keep the failed file tree available, and restore the complete old tree atomically.
        replacement=self.files_dir.with_name(self.files_dir.name+'.restore')
        if replacement.exists(): raise SourceError('恢复暂存目录已存在，请由运维检查。')
        shutil.copytree(backup/'files',replacement)
        failed=self.files_dir.with_name(self.files_dir.name+'.failed-'+str(int(time.time())))
        self.files_dir.rename(failed); replacement.rename(self.files_dir)
        self.restore_deployment(backup,metadata)


class NativeDeployment(Deployment):
    def __init__(self,config):
        super().__init__(config)
        native=config['native']
        self.current_link=Path(native['current_link'])
        self.pid_file=Path(native['pid_file'])
        self.settings_file=Path(native['settings_file'])
        self.dotnet=self.tools.get('dotnet',native.get('dotnet','dotnet'))
        self.process=None
    def preflight(self):
        if self.current_link.exists() and not self.current_link.is_symlink():
            raise SourceError('本机更新需要由更新程序管理的版本链接，不能直接覆盖项目目录。')
        if not self.settings_file.is_file(): raise SourceError('缺少本机部署配置文件。')
        writable_directory(self.current_link.parent)
        writable_directory(self.settings_file.parent)
        if not os.access(self.settings_file,os.R_OK|os.W_OK): raise SourceError('运行配置文件不可读写。')
        return super().preflight()
    def environment(self,*,validation=False,database=None,server_port=None,metric_port=None):
        env={key:value for key,value in os.environ.items() if not key.startswith(('GZCTF_','ASPNETCORE_'))}
        env['ASPNETCORE_ENVIRONMENT']='Production'
        env['GZCTF_BIND_ADDRESS']='127.0.0.1'
        native=self.config['native']
        env['GZCTF_SERVER_PORT']=str(native.get('server_port',8080))
        env['GZCTF_METRIC_PORT']=str(native.get('metric_port',3000))
        env['GZCTF_DataProtection__ApplicationName']=native.get('data_protection_application_name',str(self.current_link).rstrip('/')+'/')
        if validation:
            env.update(GZCTF_UPDATE_VALIDATION='true',GZCTF_SERVER_PORT=str(server_port),GZCTF_METRIC_PORT=str(metric_port),
                GZCTF_ConnectionStrings__Database=self.database.connection(database),GZCTF_ConnectionStrings__RedisCache='',
                GZCTF_ConnectionStrings__Storage='disk://path=./files',
                GZCTF_Updater__SocketPath='',GZCTF_Updater__TokenFile='',GZCTF_Telemetry__Enable='false')
        return env
    def prepare_runtime(self,artifact,directory,files):
        shutil.copytree(artifact,directory)
        shutil.copy2(self.settings_file,directory/'appsettings.json')
        os.chmod(directory/'appsettings.json',0o600)
        (directory/'files').symlink_to(files,target_is_directory=True)
    def smoke(self,artifact,workspace,database):
        directory=workspace/'smoke'; files=workspace/'smoke-files'
        shutil.copytree(self.files_dir,files)
        self.prepare_runtime(artifact,directory,files)
        server_port=free_port(); metric_port=free_port()
        with (workspace/'smoke.log').open('wb') as output:
            proc=subprocess.Popen([self.dotnet,str(directory/'GZCTF.dll')],cwd=directory,
                env=self.environment(validation=True,database=database,server_port=server_port,metric_port=metric_port),
                stdout=output,stderr=subprocess.STDOUT,stdin=subprocess.DEVNULL,start_new_session=True)
            try:
                await_health(f'http://127.0.0.1:{metric_port}/healthz',process=proc)
                with urllib.request.urlopen(f'http://127.0.0.1:{metric_port}/update-validation',timeout=5) as response:
                    report=json.load(response)
                if report.get('project')!='GZCTF' or not set(self.migrations()).issubset(report.get('migrations',[])):
                    raise SourceError('候选程序未实现兼容性检查，或不包含当前数据库所需的迁移。')
            finally:
                if proc.poll() is None: os.killpg(proc.pid,signal.SIGTERM)
                try: proc.wait(timeout=15)
                except subprocess.TimeoutExpired: os.killpg(proc.pid,signal.SIGKILL); proc.wait(timeout=10)
    def current(self):
        try:
            manifest=json.loads((self.current_link/'gzctf-release.json').read_text())
            return manifest.get('revision') or manifest.get('version')
        except (OSError,ValueError): return None
    def _ports_available(self):
        for port in (self.config['native'].get('server_port',8080),self.config['native'].get('metric_port',3000)):
            with socket.socket() as sock:
                sock.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
                try: sock.bind(('127.0.0.1',port))
                except OSError as error: raise SourceError('应用端口仍被占用，缺少可信进程记录或应用尚未停止。') from error
    def stop(self):
        if not self.pid_file.exists():
            self._ports_available()
            return
        record=json.loads(self.pid_file.read_text()); pid=int(record['pid'])
        try:
            args=run(['ps','-p',str(pid),'-o','args=']).decode()
            if record['assembly'] not in args or os.getpgid(pid)!=pid:
                raise SourceError('应用进程标识不匹配，已取消停止操作。')
            os.killpg(pid,signal.SIGTERM)
            deadline=time.monotonic()+30
            while time.monotonic()<deadline:
                try: os.kill(pid,0)
                except ProcessLookupError: break
                # A reaped child can remain as a zombie until its owning agent returns.
                state=run(['ps','-p',str(pid),'-o','stat=']).decode().strip()
                if state.startswith('Z'): break
                time.sleep(.2)
            else: raise SourceError('应用未能正常停止，已取消替换。')
        except ProcessLookupError: pass
        except SourceError:
            # ps returns nonzero when a recorded process has already exited.
            try: os.kill(pid,0)
            except ProcessLookupError: pass
            else: raise
        self.pid_file.unlink(missing_ok=True)
        self._ports_available()
    def deployment_metadata(self):
        shutil.copy2(self.settings_file,self._backup_destination/'appsettings.json') if hasattr(self,'_backup_destination') else None
        return {'release':str(self.current_link.resolve()) if self.current_link.is_symlink() else None}
    def backup(self,destination):
        self._backup_destination=destination
        return super().backup(destination)
    def activate(self,artifact):
        release=artifact.parent/'release'
        self.prepare_runtime(artifact,release,self.files_dir)
        self._switch(release)
    def _switch(self,release):
        temporary=self.current_link.with_name(self.current_link.name+'.next')
        temporary.unlink(missing_ok=True); temporary.symlink_to(release,target_is_directory=True)
        os.replace(temporary,self.current_link)
    def restore_deployment(self,backup,metadata):
        if not metadata.get('release'): raise SourceError('没有旧版本路径可恢复。')
        shutil.copy2(backup/'appsettings.json',self.settings_file)
        self._switch(Path(metadata['release']))
    def start(self):
        directory=self.current_link.resolve(); assembly=directory/'GZCTF.dll'
        if not assembly.is_file(): raise SourceError('活动版本缺少 GZCTF.dll。')
        if self.pid_file.exists():
            record=json.loads(self.pid_file.read_text()); pid=int(record['pid'])
            try: os.kill(pid,0)
            except ProcessLookupError: self.pid_file.unlink()
            else:
                args=run(['ps','-p',str(pid),'-o','args=']).decode()
                if record.get('assembly')==str(assembly) and str(assembly) in args and os.getpgid(pid)==pid:
                    return
                raise SourceError('已有进程记录与当前版本不匹配，请先停止原应用。')
        self._ports_available()
        self.pid_file.parent.mkdir(parents=True,exist_ok=True,mode=0o700)
        with (self.pid_file.parent/'application.log').open('ab') as output:
            proc=subprocess.Popen([self.dotnet,str(assembly)],cwd=directory,env=self.environment(),
                stdout=output,stderr=subprocess.STDOUT,stdin=subprocess.DEVNULL,start_new_session=True)
        self.process=proc
        self.pid_file.write_text(json.dumps({'pid':proc.pid,'assembly':str(assembly)})); os.chmod(self.pid_file,0o600)
    def health(self): await_health(f"http://127.0.0.1:{self.config['native'].get('metric_port',3000)}/healthz",process=self.process,timeout=120)


class ComposeDeployment(Deployment):
    def __init__(self,config):
        super().__init__(config)
        for key in ('compose_file','env_file'):
            if not Path(config[key]).is_absolute(): raise ValueError(f'{key} must be absolute')
        self.app=config.get('app_service','app')
        if not re.fullmatch('[a-z][a-z0-9_-]*',self.app): raise ValueError('Invalid application service')
    def compose(self,*args,timeout=120):
        return run(['docker','compose','--project-directory',str(Path(self.config['compose_file']).parent),
            '--env-file',self.config['env_file'],'-f',self.config['compose_file'],*args],timeout=timeout)
    def preflight(self):
        detail=super().preflight()
        for key in ('compose_file','env_file','settings_file'):
            path=Path(self.config[key])
            if not path.is_file() or not os.access(path,os.R_OK): raise SourceError('Compose 部署配置不可读取。')
            if key!='compose_file':
                writable_directory(path.parent)
                if not os.access(path,os.W_OK): raise SourceError('Compose 运行配置不可替换。')
        self.compose('config','--quiet')
        return detail
    def image(self,artifact): return 'gzctf-managed:'+tree_digest(artifact)
    def ensure_image(self,artifact):
        image=self.image(artifact)
        dockerfile='''FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache wget libpcap icu-data-full icu-libs ca-certificates libgdiplus tzdata krb5-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY . .
ENTRYPOINT ["dotnet","GZCTF.dll"]
'''
        # Dockerfile lives outside the immutable artifact.
        path=artifact.parent/'runtime.Dockerfile'; path.write_text(dockerfile)
        run(['docker','build','--platform','linux/arm64' if self.runtime.endswith('arm64') else 'linux/amd64',
             '-f',path,'-t',image,artifact],timeout=900)
        return image
    def smoke(self,artifact,workspace,database):
        image=self.ensure_image(artifact); name='gzctf-check-'+workspace.name[:16]
        db_info=json.loads(run(['docker','inspect',self.database.container]))[0]
        networks=db_info['NetworkSettings']['Networks']
        network=self.config.get('validation_network') or next(iter(networks))
        connection=connection_string(self.database.container,5432,database,self.database.user,self.config['database']['password'])
        files=workspace/'smoke-files'; shutil.copytree(self.files_dir,files)
        settings=Path(self.config['settings_file'])
        try:
            run(['docker','run','-d','--name',name,'--network',network,
                '--mount',f'type=bind,src={files},dst=/app/files',
                '--mount',f'type=bind,src={settings},dst=/app/appsettings.json,readonly',
                '-e','GZCTF_UPDATE_VALIDATION=true','-e',f'GZCTF_ConnectionStrings__Database={connection}',
                '-e','GZCTF_ConnectionStrings__RedisCache=','-e','GZCTF_ConnectionStrings__Storage=disk://path=./files',
                '-e','GZCTF_Updater__SocketPath=',
                '-e','GZCTF_Updater__TokenFile=',image])
            for _ in range(120):
                try:
                    value=run(['docker','exec',name,'wget','-qO-','http://127.0.0.1:3000/update-validation']).decode()
                    report=json.loads(value)
                    if report.get('project')=='GZCTF' and set(self.migrations()).issubset(report.get('migrations',[])): return
                    raise SourceError('候选版本未包含当前数据库迁移。')
                except (ValueError,SourceError): time.sleep(1)
            raise SourceError('候选镜像未通过隔离试运行。')
        finally: run(['docker','rm','-f',name])
    def current(self):
        try:
            return re.search(r'^GZCTF_IMAGE=(.+)$',Path(self.config['env_file']).read_text(),re.M).group(1)
        except (OSError,AttributeError): return None
    def stop(self): self.compose('stop',self.app)
    def deployment_metadata(self):
        shutil.copy2(self.config['env_file'],self._backup_destination/'deployment.env')
        shutil.copy2(self.config['settings_file'],self._backup_destination/'appsettings.json')
        return {'image':self.current()}
    def backup(self,destination): self._backup_destination=destination; return super().backup(destination)
    def activate(self,artifact):
        image=self.image(artifact) # Built and smoke-tested before downtime.
        path=Path(self.config['env_file']); original=path.read_text()
        if len(re.findall(r'^GZCTF_IMAGE=.*$',original,re.M))!=1: raise SourceError('部署配置必须包含唯一 GZCTF_IMAGE。')
        temporary=path.with_suffix('.tmp')
        temporary.write_text(re.sub(r'^GZCTF_IMAGE=.*$',f'GZCTF_IMAGE={image}',original,flags=re.M))
        os.chmod(temporary,path.stat().st_mode & 0o777); os.replace(temporary,path)
    def restore_deployment(self,backup,metadata):
        shutil.copy2(backup/'deployment.env',self.config['env_file'])
        shutil.copy2(backup/'appsettings.json',self.config['settings_file'])
    def start(self): self.compose('up','-d','--no-deps',self.app,timeout=180)
    def health(self):
        for _ in range(120):
            try:
                if self.compose('exec','-T',self.app,'wget','-qO-','http://127.0.0.1:3000/healthz').strip()==b'Healthy': return
            except SourceError: pass
            time.sleep(1)
        raise SourceError('新版本未通过健康检查。')


def create_deployment(config):
    mode=config.get('deployment_mode')
    if mode=='native': return NativeDeployment(config)
    if mode=='compose': return ComposeDeployment(config)
    raise ValueError('deployment_mode must be native or compose')
