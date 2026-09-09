import socket
import json
import os
import subprocess
import sys

class MTKBridge:
    def __init__(self, host='127.0.0.1', port=9876):
        self.host = host
        self.port = port
        self.server = None
        
        # mtk.py ဖိုင်ရှိတဲ့ နေရာကို ရှာဖွေခြင်း
        self.mtk_script = os.path.join(os.path.dirname(__file__), 'mtk.py')
        if not os.path.exists(self.mtk_script):
            self.mtk_script = os.path.join(os.path.dirname(__file__), '..', 'mtk', 'mtk.py')

    def start_server(self):
        self.server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server.bind((self.host, self.port))
        self.server.listen(1)
        print(f"Bridge Server started on {self.host}:{self.port}", flush=True)
        print(f"Using mtk script: {self.mtk_script}", flush=True)
        
        while True:
            conn, addr = self.server.accept()
            print(f"Client connected: {addr}", flush=True)
            self.handle_client(conn)

    def handle_client(self, conn):
        try:
            while True:
                data = conn.recv(4096).decode('utf-8')
                if not data:
                    break
                
                command = json.loads(data)
                response = self.execute_command(command)
                conn.send(json.dumps(response).encode('utf-8'))
        except Exception as e:
            print(f"Client error: {e}", flush=True)
        finally:
            conn.close()

    def execute_command(self, command):
        cmd_type = command.get('type')
        params = command.get('params', {})
        
        if cmd_type == 'connect':
            # Connect ကို စမ်းသပ်ခြင်း - mtk.py ိုင် ရှိ/မရှိ စစ်ပါ
            if os.path.exists(self.mtk_script):
                return {
                    'success': True, 
                    'message': 'Ready', 
                    'info': {'chipset': 'Auto-Detect', 'hw_code': 'Auto-Detect', 'storage': 'Auto-Detect'}
                }
            else:
                return {'success': False, 'error': f'mtk.py not found at {self.mtk_script}'}
        
        elif cmd_type in ['read_partition', 'write_partition', 'erase_partition']:
            partition = params.get('partition')
            
            if cmd_type == 'read_partition':
                output = params.get('output')
                args = ['python', '-u', self.mtk_script, 'r', partition, output]
            elif cmd_type == 'write_partition':
                input_file = params.get('input')
                args = ['python', '-u', self.mtk_script, 'w', partition, input_file]
            elif cmd_type == 'erase_partition':
                args = ['python', '-u', self.mtk_script, 'e', partition]
            
            try:
                # subprocess.Popen ြင့် real-time output ဖတ်ခြင်း
                process = subprocess.Popen(
                    args,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    bufsize=1,
                    universal_newlines=True
                )
                
                last_progress = ""
                output_lines = []
                
                # Real-time output ဖတ်ခြင်း
                for line in process.stdout:
                    line = line.strip()
                    output_lines.append(line)
                    
                    # Progress line ကို သိမ်းခြင်း
                    if 'Progress:' in line or '%' in line or 'Read:' in line:
                        last_progress = line
                        print(f"[Progress] {line}", flush=True)
                    
                    # Command ပြီးပြီလား စစ်ဆေးခြင်း
                    if 'Done' in line or 'completed' in line.lower() or 'success' in line.lower():
                        break
                
                process.wait(timeout=3600)
                
                if process.returncode == 0:
                    size = os.path.getsize(output) if cmd_type == 'read_partition' and os.path.exists(output) else 0
                    return {
                        'success': True, 
                        'message': f'{cmd_type} {partition} success', 
                        'size': size,
                        'progress': last_progress,
                        'output': output_lines[-5:]  # ောက်ဆုံး output 5 ကြောင်း
                    }
                else:
                    error_msg = '\n'.join(output_lines[-10:]) if output_lines else 'Command failed'
                    return {'success': False, 'error': error_msg, 'progress': last_progress}
                    
            except subprocess.TimeoutExpired:
                process.kill()
                return {'success': False, 'error': 'Command timed out', 'progress': last_progress}
            except Exception as e:
                return {'success': False, 'error': str(e)}
        
        return {'success': False, 'error': 'Unknown command'}

if __name__ == '__main__':
    print("Starting MTK Bridge Server...", flush=True)
    bridge = MTKBridge()
    bridge.start_server()