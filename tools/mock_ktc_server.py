# Development aid: a tiny fake KTC REST API (Login / Relogin / Logoff + echo endpoints) on http://127.0.0.1:9099/KTCRESTAPI
# Use it to try the explorer without a real KTC server. Password for Login is "Secret123". Not part of the product.
import json, time, base64
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
TOKENS = {}
def jwt(exp):
    b=lambda d: base64.urlsafe_b64encode(json.dumps(d).encode()).rstrip(b'=').decode()
    return b({"alg":"none"})+"."+b({"exp":exp})+".sig"
class H(BaseHTTPRequestHandler):
    def log_message(self,*a): pass
    def send(self,code,obj,ctype="application/json"):
        body=obj if isinstance(obj,bytes) else json.dumps(obj).encode()
        self.send_response(code); self.send_header("Content-Type",ctype); self.send_header("Content-Length",str(len(body))); self.send_header("X-Mock","1"); self.end_headers(); self.wfile.write(body)
    def do_any(self):
        n=int(self.headers.get("Content-Length") or 0); body=self.rfile.read(n).decode() if n else ""
        path=self.path.split("?")[0].rstrip("/")
        print(self.command,self.path,"auth=",(self.headers.get("Authorization") or "")[:22],flush=True)
        if path.endswith("/api/v1/Login") and self.command=="POST":
            d=json.loads(body)
            if d.get("Password")!="Secret123": return self.send(401,{"Message":"bad credentials"})
            life=int(open("/tmp/ktc_life").read()) if __import__("os").path.exists("/tmp/ktc_life") else 1200
            return self.send(200,{"access_token":jwt(int(time.time())+life),"ktc_access_token":"ktc-"+str(time.time()),"expires_in":life,"token_type":"bearer","userName":d["Username"]})
        if path.endswith("/api/v1/Relogin") and self.command=="POST":
            life=int(open("/tmp/ktc_life").read()) if __import__("os").path.exists("/tmp/ktc_life") else 1200
            print("  RELOGIN",flush=True)
            return self.send(200,{"access_token":jwt(int(time.time())+life),"ktc_access_token":"ktc-r","expires_in":life})
        if path.endswith("/api/v1/Logoff"): return self.send(200,{})
        if not (self.headers.get("Authorization") or "").startswith("Bearer "): return self.send(401,{"Message":"no token"})
        if "/Action/" in path and self.command=="GET": return self.send(200,{"Id":int(path.split("/")[-1]),"Status":"Pending","Password":"p@ss","retry":0,"stamp":time.time()})
        if path.endswith("/boom"): return self.send(500,{"Message":"kaboom"})
        if "File" in path: return self.send(200,b"\x00\x01\x02binary","application/octet-stream")
        return self.send(200,{"ok":True,"method":self.command,"echo":json.loads(body) if body.startswith("{") else body,"query":self.path.partition("?")[2]})
    do_GET=do_POST=do_PUT=do_DELETE=do_HEAD=do_any
ThreadingHTTPServer(("127.0.0.1",9099),H).serve_forever()
