# mtk_bridge.py - MTK Connection Bridge Server
import socket
import json
import sys
import os

# mtkclient folder path ကို ထည့်ပါ
mtkclient_path = os.path.join(os.path.dirname(__file__), 'mtkclient')
if os.path.exists(mtkclient_path):
    sys.path.insert(0, mtkclient_path)

class MTKBridge:
    def __init__(self, host='127.0.0.1', port=9876):
        self.host = host
        self.port = port
        self.mtk = None
        self.connected = False
        self.server = None
        
    def start_server(self):
        """TCP Server စတင်ခြင်း"""
        self.server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server.bind((self.host, self.port))
        self.server.listen(1)
        print(f"Bridge Server started on {self.host}:{self.port}")
        print("Waiting for C# client connection...")
        
        while True:
            conn, addr = self.server.accept()
            print(f"Client connected: {addr}")
            self.handle_client(conn)
    
    def handle_client(self, conn):
        """Client (C# App) ကနေ command တွေကို ကိုင်တွယ်ခြင်း"""
        try:
            while True:
                data = conn.recv(4096).decode('utf-8')
                if not data:
                    break
                
                command = json.loads(data)
                response = self.execute_command(command)
                conn.send(json.dumps(response).encode('utf-8'))
        except Exception as e:
            print(f"Client error: {e}")
        finally:
            conn.close()
    
    def execute_command(self, command):
        """Command ကို execute လုပ်ခြင်း"""
        cmd_type = command.get('type')
        params = command.get('params', {})
        
        try:
            if cmd_type == 'connect':
                return self.connect_device(params)
            elif cmd_type == 'disconnect':
                return self.disconnect_device()
            elif cmd_type == 'read_partition':
                return self.read_partition(params)
            elif cmd_type == 'write_partition':
                return self.write_partition(params)
            elif cmd_type == 'erase_partition':
                return self.erase_partition(params)
            elif cmd_type == 'get_info':
                return self.get_device_info()
            else:
                return {'success': False, 'error': 'Unknown command'}
        except Exception as e:
            return {'success': False, 'error': str(e)}
    
    def connect_device(self, params):
        """Device ကို ျိတ်ဆက်ခြင်း"""
        if self.connected:
            return {'success': True, 'message': 'Already connected'}
        
        try:
            from mtk import MTK
            self.mtk = MTK()
            self.mtk.port.connect()
            self.connected = True
            
            return {
                'success': True,
                'message': 'Device connected',
                'info': {
                    'chipset': getattr(self.mtk, 'chipset', 'Unknown'),
                    'hw_code': getattr(self.mtk, 'hw_code', 'Unknown'),
                    'storage': getattr(self.mtk, 'storage_type', 'Unknown')
                }
            }
        except Exception as e:
            return {'success': False, 'error': f'Connection failed: {str(e)}'}
    
    def disconnect_device(self):
        """Device ကို ဖြုတ်ခြင်း"""
        if self.mtk:
            try:
                self.mtk.port.close()
            except:
                pass
        self.connected = False
        return {'success': True, 'message': 'Device disconnected'}
    
    def read_partition(self, params):
        """Partition ဖတ်ခြင်း"""
        if not self.connected:
            return {'success': False, 'error': 'Not connected'}
        
        partition = params.get('partition')
        output_file = params.get('output')
        
        try:
            self.mtk.read_partition(partition, output_file)
            file_size = os.path.getsize(output_file) if os.path.exists(output_file) else 0
            
            return {
                'success': True,
                'message': f'Read {partition} successfully',
                'file': output_file,
                'size': file_size
            }
        except Exception as e:
            return {'success': False, 'error': f'Read failed: {str(e)}'}
    
    def write_partition(self, params):
        """Partition ရေးခြင်း"""
        if not self.connected:
            return {'success': False, 'error': 'Not connected'}
        
        partition = params.get('partition')
        input_file = params.get('input')
        
        try:
            self.mtk.write_partition(partition, input_file)
            return {'success': True, 'message': f'Wrote {partition} successfully'}
        except Exception as e:
            return {'success': False, 'error': f'Write failed: {str(e)}'}
    
    def erase_partition(self, params):
        """Partition ဖျက်ခြင်း"""
        if not self.connected:
            return {'success': False, 'error': 'Not connected'}
        
        partition = params.get('partition')
        
        try:
            self.mtk.erase_partition(partition)
            return {'success': True, 'message': f'Erased {partition} successfully'}
        except Exception as e:
            return {'success': False, 'error': f'Erase failed: {str(e)}'}
    
    def get_device_info(self):
        """Device Info ရယူခြင်း"""
        if not self.connected:
            return {'success': False, 'error': 'Not connected'}
        
        return {
            'success': True,
            'info': {
                'chipset': getattr(self.mtk, 'chipset', 'Unknown'),
                'hw_code': getattr(self.mtk, 'hw_code', 'Unknown'),
                'storage_type': getattr(self.mtk, 'storage_type', 'Unknown')
            }
        }

if __name__ == '__main__':
    print("Starting MTK Bridge Server...")
    bridge = MTKBridge()
    bridge.start_server()