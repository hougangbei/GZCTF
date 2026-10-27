#!/usr/bin/env python3
"""Generate a TCP port-pool gateway and optionally merge an updated GZCTF config (Python 3.8+)."""
import argparse
import datetime
import ipaddress
import json
import os
import re
import shutil
import tempfile
from pathlib import Path

IMAGE = 'traefik:v3.7.16'

def host(value):
    if not value or value != value.strip():
        raise ValueError('Entry must be a bare IP or DNS hostname without a scheme, path, or port')
    try:
        return str(ipaddress.IPv4Address(value))
    except ipaddress.AddressValueError:
        if re.fullmatch(r'[0-9.]+', value):
            raise ValueError('Invalid IPv4 address')
    if len(value) > 253 or not all(re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?', part) for part in value.split('.')):
        raise ValueError('Entry must be a bare IPv4 address or DNS hostname without scheme/path/port')
    return value

def bounds(start, end):
    if not 1024 <= start <= end <= 65535 or end - start + 1 > 1000:
        raise ValueError('Choose an ascending pool within 1024-65535, at most 1000 ports')

def configuration(worker, start, end):
    worker = host(worker)
    bounds(start, end)
    static = {'entryPoints': {}, 'providers': {'file': {'directory': '/etc/traefik/dynamic-pool', 'watch': True}}, 'log': {'level': 'INFO'}, 'global': {'checkNewVersion': False, 'sendAnonymousUsage': False}}
    dynamic = {'tcp': {'routers': {}, 'services': {}}}
    for port in range(start, end + 1):
        key = 'p' + str(port)
        static['entryPoints'][key] = {'address': ':' + str(port) + '/tcp'}
        dynamic['tcp']['routers'][key] = {'entryPoints': [key], 'rule': 'HostSNI(`*`)', 'service': key}
        dynamic['tcp']['services'][key] = {'loadBalancer': {'servers': [{'address': worker + ':' + str(port)}]}}
    compose = {'services': {'traefik': {'image': IMAGE, 'restart': 'unless-stopped', 'network_mode': 'host',
        'command': ['--configfile=/etc/traefik/traefik.yml'],
        'volumes': ['./traefik.yml:/etc/traefik/traefik.yml:ro', './dynamic-pool:/etc/traefik/dynamic-pool:ro'],
        'read_only': True, 'cap_drop': ['ALL'], 'security_opt': ['no-new-privileges:true'],
        'logging': {'driver': 'json-file', 'options': {'max-size': '10m', 'max-file': '3'}}}}}
    return static, dynamic, compose

def backup(path):
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    target = path.with_name(path.name + '.before-port-pool-' + stamp)
    shutil.copy2(str(path), str(target))
    os.chmod(str(target), 0o600)
    return target

def write_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=str(path.parent), delete=False) as target:
            temporary = target.name
            os.fchmod(target.fileno(), 0o600)
            json.dump(data, target, ensure_ascii=False, indent=2)
            target.write('\n')
        os.replace(temporary, str(path))
    finally:
        if temporary and os.path.exists(temporary): os.unlink(temporary)

def merged_settings(path, entry, start, end):
    entry = host(entry)
    bounds(start, end)
    with Path(path).open() as source: data = json.load(source)
    if not isinstance(data, dict): raise ValueError('appsettings must be a JSON object')
    provider = data.setdefault('ContainerProvider', {})
    if not isinstance(provider, dict) or provider.get('Type', 'Docker') != 'Docker':
        raise ValueError('This setup requires a Docker ContainerProvider')
    docker = provider.setdefault('DockerConfig', {})
    if not isinstance(docker, dict): raise ValueError('DockerConfig must be a JSON object')
    if not docker.get('Uri'): raise ValueError('Existing Docker control Uri is missing; configure the worker tunnel first')
    docker['HostPortRange'] = str(start) + '-' + str(end)
    provider['PublicEntry'] = entry
    provider['PortMappingType'] = 'Default'
    return data

def merge_appsettings(path, entry, start, end):
    path = Path(path)
    data = merged_settings(path, entry, start, end)
    saved = backup(path)
    write_json(path, data)
    return saved

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker', default='192.168.1.160')
    parser.add_argument('--port-start', type=int, default=30000)
    parser.add_argument('--port-end', type=int, default=30199)
    parser.add_argument('--output', required=True)
    parser.add_argument('--appsettings')
    parser.add_argument('--entry', help='Campus-reachable gateway IP/hostname, without scheme or port')
    args = parser.parse_args()
    try:
        static, dynamic, compose = configuration(args.worker, args.port_start, args.port_end)
        if args.appsettings:
            if not args.entry: raise ValueError('--entry is required with --appsettings')
            # Validate before generating files or changing private settings.
            merged_settings(args.appsettings, args.entry, args.port_start, args.port_end)
        output = Path(args.output).resolve()
        files = [(output / 'traefik.yml', static), (output / 'dynamic-pool' / 'pool.yml', dynamic), (output / 'compose.yml', compose)]
        for path, data in files:
            if path.exists(): backup(path)
            write_json(path, data)
        if args.appsettings:
            saved = merge_appsettings(args.appsettings, args.entry, args.port_start, args.port_end)
            print('Private appsettings backup: ' + str(saved))
        print('Generated %d TCP listeners: %d-%d -> %s, same port' % (args.port_end - args.port_start + 1, args.port_start, args.port_end, args.worker))
        print('Recreate Traefik with: docker compose -p nynuctftraefik -f compose.yml up -d --force-recreate --pull never')
        print('Use Linux host networking. Also recreate the GZCTF app after merging settings.')
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + '\n')

if __name__ == '__main__': main()
