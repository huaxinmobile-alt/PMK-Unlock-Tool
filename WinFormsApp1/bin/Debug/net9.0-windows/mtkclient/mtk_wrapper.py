
import os
import subprocess
import re
import sys

def run_mtk_command(command_args):
    mtk_script = os.path.join(os.path.dirname(__file__), 'mtk.py')
    args = [sys.executable or 'python', '-u', mtk_script] + command_args
    
    startupinfo = subprocess.STARTUPINFO()
    startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    
    try:
        process = subprocess.run(
            args, 
            capture_output=True, 
            text=True, 
            encoding='utf-8', 
            errors='ignore',
            timeout=3600,
            startupinfo=startupinfo,
            creationflags=subprocess.CREATE_NO_WINDOW
        )
        
        file_size = 0
        if len(command_args) > 2 and os.path.exists(command_args[-1]):
            try:
                file_size = os.path.getsize(command_args[-1])
            except:
                file_size = 0
            
        return {
            'success': process.returncode == 0,
            'output': process.stdout or '',
            'error': process.stderr or '',
            'size': file_size
        }
    except subprocess.TimeoutExpired:
        return {'success': False, 'output': '', 'error': 'Command timed out', 'size': 0}
    except Exception as e:
        return {'success': False, 'output': '', 'error': str(e), 'size': 0}
