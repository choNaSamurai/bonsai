import http.server, os, sys, urllib.parse
ROOT=sys.argv[1]; OUT=sys.argv[2]
class H(http.server.SimpleHTTPRequestHandler):
    def __init__(s,*a,**k): super().__init__(*a,directory=ROOT,**k)
    def do_POST(s):
        q=urllib.parse.parse_qs(urllib.parse.urlparse(s.path).query); name=os.path.basename(q['name'][0]); sub=q.get('dir',['golden'])[0]
        assert sub in ('golden','refs')
        n=int(s.headers['Content-Length']); data=s.rfile.read(n)
        os.makedirs(os.path.join(OUT,sub),exist_ok=True)
        open(os.path.join(OUT,sub,name),'wb').write(data)
        s.send_response(200); s.end_headers(); s.wfile.write(b'ok')
    def log_message(s,*a): pass
http.server.ThreadingHTTPServer(('127.0.0.1',8766),H).serve_forever()
