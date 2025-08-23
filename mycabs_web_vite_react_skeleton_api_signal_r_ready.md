# mycabs-web – Vite React skeleton (API + SignalR ready)

A ready-to-run frontend scaffold wired for **Redux Toolkit**, **TanStack Query**, **Axios** (with JWT), **SignalR** (Notifications + Admin), **React-Bootstrap**, **Toastify**, and **React Router**. Mobile‑responsive layout + 2 sample pages (Login, Notifications) and an Admin Dashboard stub.

---

## 0) Quick start

```bash
# 1) create project
npm create vite@latest mycabs-web -- --template react
cd mycabs-web

# 2) install deps
npm i react-router-dom @reduxjs/toolkit react-redux @tanstack/react-query axios @microsoft/signalr \
      react-bootstrap bootstrap react-toastify dayjs clsx formik yup

# (optional) query devtools
npm i -D @tanstack/react-query-devtools

# 3) add files from this doc (mirror structure) and create .env.local
# 4) run
npm run dev
```

Create \`\` (or edit `.env`):

```env
VITE_API_BASE=http://localhost:5000
# if different host/port for websockets, set this; otherwise will reuse API_BASE
VITE_SIGNALR_BASE=http://localhost:5000
```

> The backend uses **Bearer JWT** and SignalR hubs at `/hubs/notifications` and `/hubs/admin` (Admin-only).

---

## 1) File tree

```
src/
  main.jsx
  App.jsx
  styles.css
  lib/
    axios.js
    signalr.js
    queryClient.js
  store/
    index.js
    authSlice.js
    uiSlice.js
  components/
    AppShell.jsx
    Protected.jsx
  pages/
    Login.jsx
    Notifications.jsx
    AdminDashboard.jsx
```

Also add Bootstrap + Toastify CSS in `index.html`/`main.jsx`.

---

## 2) index.html (inject root & meta)

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>MyCabs</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.jsx"></script>
  </body>
</html>
```

---

## 3) src/main.jsx

```jsx
import React from 'react'
import ReactDOM from 'react-dom/client'
import { Provider } from 'react-redux'
import { QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { ToastContainer } from 'react-toastify'
import 'react-toastify/dist/ReactToastify.css'
import 'bootstrap/dist/css/bootstrap.min.css'
import './styles.css'

import App from './App'
import { store } from './store'
import { queryClient } from './lib/queryClient'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <Provider store={store}>
      <QueryClientProvider client={queryClient}>
        <App />
        <ToastContainer position="top-right" autoClose={2500} />
        {import.meta.env.DEV ? <ReactQueryDevtools initialIsOpen={false} /> : null}
      </QueryClientProvider>
    </Provider>
  </React.StrictMode>
)
```

---

## 4) src/App.jsx

```jsx
// UPDATED: thêm HomeRedirect + routes Register & OTP pages; tránh redirect loop khi chưa login
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useSelector } from 'react-redux'
import AppShell from './components/AppShell'
import Login from './pages/Login'
import Register from './pages/Register'
import Notifications from './pages/Notifications'
import AdminDashboard from './pages/AdminDashboard'
import Protected from './components/Protected'
// NEW: OTP pages
import OtpRequest from './pages/OtpRequest'
import OtpVerify from './pages/OtpVerify'
import OtpReset from './pages/OtpReset'

// UPDATED: Quyết định trang đích dựa theo trạng thái đăng nhập
function HomeRedirect(){
  const token = useSelector(s => s.auth.token)
  return <Navigate to={token ? '/notifications' : '/login'} replace />
}

export default function App(){
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppShell />}> 
          {/* UPDATED: dùng HomeRedirect thay vì điều hướng cứng */}
          <Route index element={<HomeRedirect />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />

          {/* NEW: OTP flows */}
          <Route path="/otp/request" element={<OtpRequest />} />
          <Route path="/otp/verify" element={<OtpVerify />} />
          <Route path="/otp/reset" element={<OtpReset />} />

          {/* Auth required */}
          <Route path="/notifications" element={<Protected><Notifications/></Protected>} />

          {/* Admin required */}
          <Route path="/admin" element={<Protected role="Admin"><AdminDashboard/></Protected>} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
```

---

## 5) src/styles.css (minimal)

```css
:root { --brand: #0d6efd; }
html, body, #root { height: 100%; }
.navbar-brand b { color: var(--brand); }
.card-kpi { min-width: 150px; }
```

---

## 6) src/store/index.js (configure store)

```js
import { configureStore } from '@reduxjs/toolkit'
import auth from './authSlice'
import ui from './uiSlice'

export const store = configureStore({
  reducer: { auth, ui },
})
```

---

## 7) src/store/authSlice.js (JWT auth + role parsing)

```js
import { createSlice } from '@reduxjs/toolkit'

function parseJwt(token){
  try { return JSON.parse(atob(token.split('.')[1])) } catch { return null }
}

function getRoleFromClaims(claims){
  if (!claims) return null
  return claims['role'] || claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || null
}

const savedToken = localStorage.getItem('accessToken')
const claims = savedToken ? parseJwt(savedToken) : null

const initialState = {
  token: savedToken || null,
  userId: claims?.sub || claims?.nameid || null,
  email: claims?.email || null,
  role: getRoleFromClaims(claims) || 'User',
}

const slice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    setCredentials(state, { payload }){
      const token = payload
      state.token = token
      const c = parseJwt(token)
      state.userId = c?.sub || c?.nameid || null
      state.email = c?.email || null
      state.role = getRoleFromClaims(c) || 'User'
      localStorage.setItem('accessToken', token)
    },
    logout(state){
      state.token = null; state.userId = null; state.email = null; state.role = 'User'
      localStorage.removeItem('accessToken')
    }
  }
})

export const { setCredentials, logout } = slice.actions
export default slice.reducer
```

---

## 8) src/store/uiSlice.js (badges/unread & simple flags)

```js
import { createSlice } from '@reduxjs/toolkit'

const slice = createSlice({
  name: 'ui',
  initialState: { unreadCount: 0, connected: false },
  reducers: {
    setUnread(state, { payload }){ state.unreadCount = payload ?? 0 },
    setConnected(state, { payload }){ state.connected = !!payload },
  }
})

export const { setUnread, setConnected } = slice.actions
export default slice.reducer
```

---

## 9) src/lib/queryClient.js

```js
import { QueryClient } from '@tanstack/react-query'

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 20_000, refetchOnWindowFocus: false },
  }
})
```

---

## 10) src/lib/axios.js (with auth interceptor)

```js
import axios from 'axios'

const api = axios.create({ baseURL: import.meta.env.VITE_API_BASE || '' })

api.interceptors.request.use(cfg => {
  const t = localStorage.getItem('accessToken')
  if (t) cfg.headers.Authorization = `Bearer ${t}`
  return cfg
})

api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401) {
      // UPDATED: KHÔNG redirect cứng để tránh vòng lặp refresh khi đang ở trang cần auth
      // location.href = '/login'
    }
    return Promise.reject(err)
  }
)

export default api
```

---

## 11) src/lib/signalr.js (singleton connections)

```js
import * as signalR from '@microsoft/signalr'

const BASE = import.meta.env.VITE_SIGNALR_BASE || import.meta.env.VITE_API_BASE || ''

let notifConn = null
let adminConn = null

export function notificationsHub(){ return notifConn }
export function adminHub(){ return adminConn }

export async function startNotificationsHub(handlers = {}){
  if (notifConn) return notifConn
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${BASE}/hubs/notifications`, {
      accessTokenFactory: () => localStorage.getItem('accessToken') || ''
    })
    .withAutomaticReconnect()
    .build()

  // default events
  if (handlers.onUnread){ conn.on('unread_count', handlers.onUnread) }
  if (handlers.onNotification){ conn.on('notification', handlers.onNotification) }
  if (handlers.onChat){ conn.on('chat.message', handlers.onChat) }

  await conn.start()
  notifConn = conn
  return conn
}

export async function startAdminHub(handlers = {}){
  if (adminConn) return adminConn
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${BASE}/hubs/admin`, {
      accessTokenFactory: () => localStorage.getItem('accessToken') || ''
    })
    .withAutomaticReconnect()
    .build()

  if (handlers.onTxNew){ conn.on('admin:tx:new', handlers.onTxNew) }

  await conn.start()
  adminConn = conn
  return conn
}

export async function stopAllHubs(){
  if (notifConn){ try { await notifConn.stop() } catch{} notifConn = null }
  if (adminConn){ try { await adminConn.stop() } catch{} adminConn = null }
}
```

---

## 12) src/components/AppShell.jsx (layout + navbar)

```jsx
import { Container, Navbar, Nav, Badge, Button } from 'react-bootstrap'
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom'
import { useDispatch, useSelector } from 'react-redux'
import { logout } from '../store/authSlice'
import { setUnread } from '../store/uiSlice'
import { startNotificationsHub, startAdminHub, stopAllHubs } from '../lib/signalr'
import api from '../lib/axios'
import { useEffect } from 'react'

export default function AppShell(){
  const { token, role } = useSelector(s => s.auth)
  const unread = useSelector(s => s.ui.unreadCount)
  const dispatch = useDispatch()
  const nav = useNavigate()
  const loc = useLocation()

  // Start hubs after login
  useEffect(() => {
    let mounted = true
    async function boot(){
      if (!token) return
      // initial unread sync
      try {
        const res = await api.get('/api/notifications/unread-count')
        const count = res?.data?.data?.count ?? 0
        if (mounted) dispatch(setUnread(count))
      } catch {}

      await startNotificationsHub({
        onUnread: ({ count }) => dispatch(setUnread(count ?? 0)),
        onNotification: (p) => console.log('notification', p),
        onChat: (m) => console.log('chat.message', m),
      })

      if (role === 'Admin'){
        await startAdminHub({ onTxNew: (tx) => console.log('admin:tx:new', tx) })
      }
    }
    boot()
    return () => { mounted = false }
  }, [token, role, dispatch])

  function doLogout(){ stopAllHubs(); dispatch(logout()); nav('/login') }

  return (
    <>
      <Navbar bg="light" expand="sm" className="mb-3">
        <Container>
          <Navbar.Brand as={Link} to="/"><b>My</b>Cabs</Navbar.Brand>
          <Navbar.Toggle aria-controls="basic-navbar-nav" />
          <Navbar.Collapse id="basic-navbar-nav">
            <Nav className="me-auto">
              {token && (<Nav.Link as={Link} to="/notifications" active={loc.pathname==='/notifications'}>Notifications {unread>0 && (<Badge bg="primary" pill className="ms-1">{unread}</Badge>)}</Nav.Link>)}
              {token && role==='Admin' && (<Nav.Link as={Link} to="/admin" active={loc.pathname==='/admin'}>Admin</Nav.Link>)}
            </Nav>
            <div className="d-flex gap-2">
              {!token ? (
                <Button size="sm" variant="outline-primary" as={Link} to="/login">Login</Button>
              ) : (
                <Button size="sm" variant="outline-danger" onClick={doLogout}>Logout</Button>
              )}
            </div>
          </Navbar.Collapse>
        </Container>
      </Navbar>
      <Container className="pb-4">
        <Outlet />
      </Container>
    </>
  )
}
```

---

## 13) src/components/Protected.jsx (route guard)

```jsx
import { useSelector } from 'react-redux'
import { Navigate, useLocation } from 'react-router-dom'

export default function Protected({ children, role }){
  const { token, role: myRole } = useSelector(s => s.auth)
  const loc = useLocation()
  if (!token) return <Navigate to="/login" state={{ from: loc }} replace />
  if (role && myRole !== role) return <Navigate to="/" replace />
  return children
}
```

---

## 14) src/pages/Login.jsx

```jsx
// UPDATED: dùng Formik + Yup để validate, giữ placeholder admin@mycabs.com
// UPDATED: thêm liên kết Forgot password? (OTP reset) + Verify email (OTP verify)
import { Card, Button } from 'react-bootstrap'
import { useDispatch } from 'react-redux'
import { setCredentials } from '../store/authSlice'
import { useNavigate, useLocation, Link } from 'react-router-dom'
import api from '../lib/axios'
import { toast } from 'react-toastify'
import { useFormik } from 'formik'
import * as Yup from 'yup'

const schema = Yup.object({
  email: Yup.string().email('Email không hợp lệ').required('Bắt buộc'),
  password: Yup.string().min(6, 'Tối thiểu 6 ký tự').required('Bắt buộc'),
})

export default function Login(){
  const dispatch = useDispatch()
  const nav = useNavigate()
  const loc = useLocation()

  const form = useFormik({
    initialValues: { email: '', password: '' },
    validationSchema: schema,
    onSubmit: async (values, { setSubmitting }) => {
      try {
        const res = await api.post('/api/auth/login', values)
        const token = res?.data?.data?.accessToken
          || res?.data?.data?.token
          || res?.data?.data
        if (!token) throw new Error('No token in response')
        dispatch(setCredentials(token))
        toast.success('Logged in!')
        const to = loc.state?.from?.pathname || '/'
        nav(to, { replace: true })
      } catch (err) {
        console.error(err)
        const msg = err?.response?.data?.error?.message || 'Login failed'
        toast.error(msg)
      } finally {
        setSubmitting(false)
      }
    }
  })

  return (
    <Card className="mx-auto" style={{maxWidth: 420}}>
      <Card.Body>
        <Card.Title>Login</Card.Title>
        <form onSubmit={form.handleSubmit} noValidate>
          <div className="mb-3">
            <label className="form-label">Email</label>
            <input
              name="email"
              type="email"
              className={`form-control ${form.touched.email && form.errors.email ? 'is-invalid' : ''}`}
              placeholder="admin@mycabs.com" // UPDATED: placeholder email admin
              value={form.values.email}
              onChange={form.handleChange}
              onBlur={form.handleBlur}
              required
            />
            {form.touched.email && form.errors.email ? (
              <div className="invalid-feedback">{form.errors.email}</div>
            ) : null}
          </div>

          <div className="mb-3">
            <label className="form-label">Password</label>
            <input
              name="password"
              type="password"
              className={`form-control ${form.touched.password && form.errors.password ? 'is-invalid' : ''}`}
              placeholder="••••••••"
              value={form.values.password}
              onChange={form.handleChange}
              onBlur={form.handleBlur}
              required
            />
            {form.touched.password && form.errors.password ? (
              <div className="invalid-feedback">{form.errors.password}</div>
            ) : null}
          </div>

          <Button type="submit" disabled={form.isSubmitting}>{form.isSubmitting ? '…' : 'Login'}</Button>
          <div className="d-flex justify-content-between small mt-3">
            <span>
              Chưa có tài khoản? <Link to="/register">Đăng ký</Link>
            </span>
            <span className="text-nowrap">
              <Link to="/otp/request?mode=reset">Quên mật khẩu?</Link> ·{' '}
              <Link to="/otp/request?mode=verify">Xác minh email</Link>
            </span>
          </div>
        </form>
      </Card.Body>
    </Card>
  )
}
```

---

## 15) src/pages/Notifications.jsx (list + mark-all)

```jsx
import { useQuery, useMutation } from '@tanstack/react-query'
import api from '../lib/axios'
import { Table, Button, Stack, Badge } from 'react-bootstrap'
import dayjs from 'dayjs'
import { toast } from 'react-toastify'

async function fetchList(){
  const res = await api.get('/api/notifications?page=1&pageSize=20')
  return res.data?.data
}

async function markAll(){
  const res = await api.post('/api/notifications/mark-all-read')
  return res.data?.data
}

export default function Notifications(){
  const { data, isLoading, refetch } = useQuery({ queryKey:['notifs'], queryFn: fetchList })
  const m = useMutation({ mutationFn: markAll, onSuccess: ()=>{ toast.success('Marked all as read'); refetch() } })

  if (isLoading) return <div>Loading…</div>
  const items = data?.items || data?.Items || []

  return (
    <Stack gap={3}>
      <div className="d-flex justify-content-between align-items-center">
        <h5 className="m-0">Notifications</h5>
        <Button size="sm" variant="outline-secondary" onClick={()=>m.mutate()} disabled={m.isPending}>Mark all read</Button>
      </div>
      <Table responsive hover size="sm">
        <thead>
          <tr>
            <th>Type</th>
            <th>Title / Message</th>
            <th>When</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {items.map((n)=>{
            const isRead = !!n.readAt || n.isRead
            return (
              <tr key={n.id || n.Id}>
                <td><Badge bg={isRead?'secondary':'primary'}>{n.type || n.Type}</Badge></td>
                <td>
                  <div className="fw-semibold">{n.title || n.Title}</div>
                  <div className="text-muted small">{n.message || n.Message}</div>
                </td>
                <td className="text-nowrap">{dayjs(n.createdAt || n.CreatedAt).fromNow?.() || dayjs(n.createdAt || n.CreatedAt).format('YYYY-MM-DD HH:mm')}</td>
                <td></td>
              </tr>
            )
          })}
        </tbody>
      </Table>
    </Stack>
  )
}
```

> Uses the envelope shape you return (`data.items` or `data.Items`). Adjust mapping if needed.

---

## 16) src/pages/AdminDashboard.jsx

```jsx
// UPDATED: Hoàn chỉnh Dashboard: KPIs + Chart (recharts) + Top Companies/Drivers
// và realtime refresh khi có giao dịch mới (Admin hub)
import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import api from '../lib/axios'
import { Row, Col, Card, Table } from 'react-bootstrap'
import { adminHub } from '../lib/signalr' // NEW: realtime
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid
} from 'recharts' // NEW: chart

async function fetchOverview(){
  const res = await api.get('/api/admin/reports/overview')
  return res.data?.data
}
async function fetchDaily(){
  const res = await api.get('/api/admin/reports/tx-daily')
  return res.data?.data
}
async function fetchTopCompanies(){
  const res = await api.get('/api/admin/reports/top-companies')
  return res.data?.data
}
async function fetchTopDrivers(){
  const res = await api.get('/api/admin/reports/top-drivers')
  return res.data?.data
}

export default function AdminDashboard(){
  const { data: ov, refetch: refOv } = useQuery({ queryKey:['admin','overview'], queryFn: fetchOverview })
  const { data: daily, refetch: refDaily } = useQuery({ queryKey:['admin','daily'], queryFn: fetchDaily })
  const { data: topC, refetch: refTopC } = useQuery({ queryKey:['admin','topC'], queryFn: fetchTopCompanies })
  const { data: topD, refetch: refTopD } = useQuery({ queryKey:['admin','topD'], queryFn: fetchTopDrivers })

  // NEW: realtime refresh khi có tx mới
  useEffect(() => {
    const conn = adminHub()
    if (!conn) return
    const onNew = () => { refOv(); refDaily(); refTopC(); refTopD() }
    conn.on('admin:tx:new', onNew)
    return () => { conn.off('admin:tx:new', onNew) }
  }, [refOv, refDaily, refTopC, refTopD])

  const dailyData = (daily || []).map(p => ({
    date: p.date || p.Date,
    count: p.count ?? p.Count ?? 0,
    amount: p.amount ?? p.Amount ?? 0
  }))

  const companies = topC?.items || topC?.Items || []
  const drivers = topD?.items || topD?.Items || []

  return (
    <>
      <Row className="g-3 mb-3">
        <Col xs={6} md={3}><Kpi title="Users" value={ov?.usersTotal ?? 0} /></Col>
        <Col xs={6} md={3}><Kpi title="Companies" value={ov?.companiesTotal ?? 0} /></Col>
        <Col xs={6} md={3}><Kpi title="Drivers" value={ov?.driversTotal ?? 0} /></Col>
        <Col xs={6} md={3}><Kpi title="Tx Amount" value={(ov?.txAmount ?? 0).toLocaleString()} /></Col>
      </Row>

      <Row className="g-3">
        <Col md={7}>
          <Card className="h-100">
            <Card.Header>Transactions – Daily</Card.Header>
            <Card.Body style={{height: 320}}>
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={dailyData}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" />
                  <YAxis yAxisId="left" />
                  <YAxis orientation="right" yAxisId="right" />
                  <Tooltip />
                  <Line type="monotone" dataKey="count" name="Count" yAxisId="left" dot={false} />
                  <Line type="monotone" dataKey="amount" name="Amount" yAxisId="right" dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </Card.Body>
          </Card>
        </Col>
        <Col md={5}>
          <Card className="mb-3">
            <Card.Header>Top Companies</Card.Header>
            <Table responsive hover size="sm" className="mb-0">
              <thead><tr><th>#</th><th>Name</th><th className="text-end">Drivers</th><th className="text-end">Tx Amount</th></tr></thead>
              <tbody>
                {companies.map((c, i) => (
                  <tr key={c.id || c.Id || i}>
                    <td>{i+1}</td>
                    <td>{c.name || c.Name}</td>
                    <td className="text-end">{c.driverCount ?? c.DriverCount ?? 0}</td>
                    <td className="text-end">{(c.txAmount ?? c.TxAmount ?? 0).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </Table>
          </Card>
          <Card>
            <Card.Header>Top Drivers</Card.Header>
            <Table responsive hover size="sm" className="mb-0">
              <thead><tr><th>#</th><th>Name</th><th className="text-end">Trips</th><th className="text-end">Tx Amount</th></tr></thead>
              <tbody>
                {drivers.map((d, i) => (
                  <tr key={d.id || d.Id || i}>
                    <td>{i+1}</td>
                    <td>{d.fullName || d.FullName}</td>
                    <td className="text-end">{d.tripCount ?? d.TripCount ?? 0}</td>
                    <td className="text-end">{(d.txAmount ?? d.TxAmount ?? 0).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </Table>
          </Card>
        </Col>
      </Row>
    </>
  )
}

function Kpi({ title, value }){
  return (
    <Card className="card-kpi">
      <Card.Body>
        <div className="text-muted small">{title}</div>
        <div className="fs-4 fw-bold">{value}</div>
      </Card.Body>
    </Card>
  )
}
```

---

### Notes / wiring

- **Axios** uses `VITE_API_BASE`. Backend runs on `http://localhost:5000` by default, matching your API.
- **SignalR** will pass `access_token` automatically via `accessTokenFactory`.
- **Admin page** requires a token whose JWT claim `role` (or `ClaimTypes.Role`) equals `Admin`.
- **Navbar unread badge** updates via realtime event `unread_count` and initial GET `/api/notifications/unread-count`.
- You can easily add a **Chat** page by reusing the `/api/chat/*` endpoints and `conn.invoke('JoinThread', threadId)` from `notifications` hub.

---

## 17) Optional helpers (dayjs relative time)

Install plugin if you want `fromNow()` formatting:

```bash
npm i dayjs
```

Then at the top of `Notifications.jsx`:

```js
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
dayjs.extend(relativeTime)
```

(Already included as a dependency in Quick start.)

---

## 18) What to change if your API envelope differs

- If your controllers return `{ data: { ... } }`, current code works. If you return raw lists, use `res.data` directly.
- Adjust the **Login** path & token extraction to match your `AuthController`.
- If CORS blocks requests, ensure your backend CORS allows `http://localhost:3000` (already present in your API setup).

---

## 19) Troubleshooting (dev)

- **Trang tự refresh liên tục**:
  1. Đảm bảo token được set đúng sau login (xem Network tab, response có `data.accessToken`).
  2. Với React 18, `StrictMode` chạy effect 2 lần ở dev → nếu bạn thấy hub connect/disconnect nhiều lần, có thể tạm \*\*bỏ \*\*\`\` trong `main.jsx` khi debug.
  3. Kiểm tra `.env.local`: `VITE_API_BASE` phải trỏ đúng `http://localhost:5000`.
- **401 sau khi login**: kiểm tra localStorage có `accessToken` hay chưa; Axios thêm header `Authorization: Bearer <token>`.

## 20) Done ✅

Skeleton đã khớp cấu trúc envelope của API (login trả `data.accessToken`). Nếu còn chỗ nào khác trả dữ liệu khác chuẩn, báo mình để mình chỉnh lại mapper ở FE nhé.

---

## 21) src/pages/Register.jsx (NEW)

```jsx
// UPDATED: thêm field role + Formik + Yup validation
import { Card, Button } from 'react-bootstrap'
import { useNavigate, Link } from 'react-router-dom'
import api from '../lib/axios'
import { toast } from 'react-toastify'
import { useFormik } from 'formik'
import * as Yup from 'yup'

const ROLES = ['User','CompanyOwner','Driver','Admin'] // Admin chỉ để test

const schema = Yup.object({
  fullName: Yup.string().trim().required('Bắt buộc'),
  email: Yup.string().email('Email không hợp lệ').required('Bắt buộc'),
  password: Yup.string().min(6,'Tối thiểu 6 ký tự').required('Bắt buộc'),
  role: Yup.string().oneOf(ROLES).required('Bắt buộc')
})

export default function Register(){
  const nav = useNavigate()

  const form = useFormik({
    initialValues: { fullName:'', email:'', password:'', role:'User' },
    validationSchema: schema,
    onSubmit: async (values, { setSubmitting }) => {
      try {
        const payload = {
          email: values.email.trim(),
          password: values.password,
          fullName: values.fullName.trim(),
          role: values.role
        }
        const res = await api.post('/api/auth/register', payload)
        if (res?.data?.success) {
          toast.success('Đăng ký thành công, vui lòng đăng nhập')
          nav('/login', { replace: true })
        } else {
          throw new Error('Register failed')
        }
      } catch (err) {
        console.error(err)
        const msg = err?.response?.data?.error?.message || 'Register failed'
        toast.error(msg)
      } finally {
        setSubmitting(false)
      }
    }
  })

  return (
    <Card className="mx-auto" style={{maxWidth: 480}}>
      <Card.Body>
        <Card.Title>Register</Card.Title>
        <form onSubmit={form.handleSubmit} noValidate>
          <div className="mb-3">
            <label className="form-label">Full name</label>
            <input
              name="fullName"
              className={`form-control ${form.touched.fullName && form.errors.fullName ? 'is-invalid' : ''}`}
              placeholder="Nguyễn Văn A"
              value={form.values.fullName}
              onChange={form.handleChange}
              onBlur={form.handleBlur}
              required
            />
            {form.touched.fullName && form.errors.fullName ? (
              <div className="invalid-feedback">{form.errors.fullName}</div>
            ) : null}
          </div>

          <div className="mb-3">
            <label className="form-label">Email</label>
            <input
              name="email"
              type="email"
              className={`form-control ${form.touched.email && form.errors.email ? 'is-invalid' : ''}`}
              placeholder="you@example.com"
              value={form.values.email}
              onChange={form.handleChange}
              onBlur={form.handleBlur}
              required
            />
            {form.touched.email && form.errors.email ? (
              <div className="invalid-feedback">{form.errors.email}</div>
            ) : null}
          </div>

          <div className="mb-3">
            <label className="form-label">Password</label>
            <input
              name="password"
              type="password"
              className={`form-control ${form.touched.password && form.errors.password ? 'is-invalid' : ''}`}
              placeholder="••••••••"
              value={form.values.password}
              onChange={form.handleChange}
              onBlur={form.handleBlur}
              required
            />
            {form.touched.password && form.errors.password ? (
              <div className="invalid-feedback">{form.errors.password}</div>
            ) : null}
          </div>

          <div className="mb-3">
            <label className="form-label">Role</label>
            <select
              name="role"
              className={`form-select ${form.touched.role && form.errors.role ? 'is-invalid' : ''}`}
              value={form.values.role}
              onChange={form.handleChange}
              onBlur={form.handleBlur}
              required
            >
              {ROLES.map(r => <option key={r} value={r}>{r}</option>)}
            </select>
            {form.touched.role && form.errors.role ? (
              <div className="invalid-feedback">{form.errors.role}</div>
            ) : (
              <div className="form-text">Chọn "Admin" chỉ để test.</div>
            )}
          </div>

          <Button type="submit" disabled={form.isSubmitting}>{form.isSubmitting ? '…' : 'Create account'}</Button>
          <div className="text-center small mt-3">
            Đã có tài khoản? <Link to="/login">Đăng nhập</Link>
          </div>
        </form>
      </Card.Body>
    </Card>
  )
}
```



---

## 22) OTP pages (NEW)

### 22.1) `src/pages/OtpRequest.jsx`

```jsx
import { Card, Button } from 'react-bootstrap'
import { useNavigate, useSearchParams, Link } from 'react-router-dom'
import { useFormik } from 'formik'
import * as Yup from 'yup'
import api from '../lib/axios'
import { toast } from 'react-toastify'

const schema = Yup.object({
  email: Yup.string().email('Email không hợp lệ').required('Bắt buộc'),
  mode: Yup.string().oneOf(['verify','reset']).default('verify')
})

export default function OtpRequest(){
  const nav = useNavigate()
  const [sp] = useSearchParams()
  const qMode = sp.get('mode') === 'reset' ? 'reset' : 'verify'
  const qEmail = sp.get('email') || ''

  const form = useFormik({
    initialValues: { email: qEmail, mode: qMode },
    validationSchema: schema,
    onSubmit: async (values, { setSubmitting }) => {
      try {
        const payload = { email: values.email.trim(), purpose: values.mode } // purpose: server có thể bỏ qua
        const res = await api.post('/api/otp/request', payload)
        if (res?.data?.success) {
          toast.success('Đã gửi OTP. Xem console của API ở môi trường dev.')
          if (values.mode === 'verify') nav(`/otp/verify?email=${encodeURIComponent(values.email)}`)
          else nav(`/otp/reset?email=${encodeURIComponent(values.email)}`)
        } else throw new Error('Request OTP failed')
      } catch (err) {
        console.error(err)
        const msg = err?.response?.data?.error?.message || 'Request OTP failed'
        toast.error(msg)
      } finally {
        setSubmitting(false)
      }
    }
  })

  return (
    <Card className="mx-auto" style={{maxWidth: 480}}>
      <Card.Body>
        <Card.Title>Yêu cầu mã OTP</Card.Title>
        <form onSubmit={form.handleSubmit} noValidate>
          <div className="mb-3">
            <label className="form-label">Email</label>
            <input name="email" type="email" className={`form-control ${form.touched.email && form.errors.email ? 'is-invalid' : ''}`} value={form.values.email} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="you@example.com" required />
            {form.touched.email && form.errors.email ? (<div className="invalid-feedback">{form.errors.email}</div>) : null}
          </div>

          <div className="mb-3">
            <label className="form-label">Mục đích</label>
            <select name="mode" className={`form-select ${form.touched.mode && form.errors.mode ? 'is-invalid' : ''}`} value={form.values.mode} onChange={form.handleChange} onBlur={form.handleBlur}>
              <option value="verify">Xác minh email</option>
              <option value="reset">Đặt lại mật khẩu</option>
            </select>
          </div>

          <Button type="submit" disabled={form.isSubmitting}>{form.isSubmitting ? '…' : 'Gửi mã'}</Button>
          <div className="small mt-3">
            Có mã rồi?{' '}
            <Link to={`/otp/verify?email=${encodeURIComponent(form.values.email)}`}>Xác minh</Link>{' '}·{' '}
            <Link to={`/otp/reset?email=${encodeURIComponent(form.values.email)}`}>Đặt lại mật khẩu</Link>
          </div>
        </form>
      </Card.Body>
    </Card>
  )
}
```

### 22.2) `src/pages/OtpVerify.jsx`

```jsx
import { Card, Button } from 'react-bootstrap'
import { useNavigate, useSearchParams, Link } from 'react-router-dom'
import { useFormik } from 'formik'
import * as Yup from 'yup'
import api from '../lib/axios'
import { toast } from 'react-toastify'

const schema = Yup.object({
  email: Yup.string().email('Email không hợp lệ').required('Bắt buộc'),
  code: Yup.string().trim().length(6, '6 ký tự').required('Bắt buộc')
})

export default function OtpVerify(){
  const nav = useNavigate()
  const [sp] = useSearchParams()
  const qEmail = sp.get('email') || ''

  const form = useFormik({
    initialValues: { email: qEmail, code: '' },
    validationSchema: schema,
    onSubmit: async (values, { setSubmitting }) => {
      try {
        const payload = { email: values.email.trim(), code: values.code.trim() }
        const res = await api.post('/api/otp/verify', payload)
        if (res?.data?.success) {
          toast.success('Xác minh email thành công. Đăng nhập lại nhé!')
          nav('/login', { replace: true })
        } else throw new Error('Verify failed')
      } catch (err) {
        console.error(err)
        const msg = err?.response?.data?.error?.message || 'Verify failed'
        toast.error(msg)
      } finally {
        setSubmitting(false)
      }
    }
  })

  return (
    <Card className="mx-auto" style={{maxWidth: 480}}>
      <Card.Body>
        <Card.Title>Xác minh email bằng OTP</Card.Title>
        <form onSubmit={form.handleSubmit} noValidate>
          <div className="mb-3">
            <label className="form-label">Email</label>
            <input name="email" type="email" className={`form-control ${form.touched.email && form.errors.email ? 'is-invalid' : ''}`} value={form.values.email} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="you@example.com" required />
            {form.touched.email && form.errors.email ? (<div className="invalid-feedback">{form.errors.email}</div>) : null}
          </div>
          <div className="mb-3">
            <label className="form-label">Mã OTP</label>
            <input name="code" className={`form-control ${form.touched.code && form.errors.code ? 'is-invalid' : ''}`} value={form.values.code} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="123456" required />
            {form.touched.code && form.errors.code ? (<div className="invalid-feedback">{form.errors.code}</div>) : null}
          </div>
          <Button type="submit" disabled={form.isSubmitting}>{form.isSubmitting ? '…' : 'Xác minh'}</Button>
          <div className="small mt-3">Chưa có mã? <Link to={`/otp/request?mode=verify&email=${encodeURIComponent(form.values.email)}`}>Gửi lại mã</Link></div>
        </form>
      </Card.Body>
    </Card>
  )
}
```

### 22.3) `src/pages/OtpReset.jsx`

```jsx
import { Card, Button } from 'react-bootstrap'
import { useNavigate, useSearchParams, Link } from 'react-router-dom'
import { useFormik } from 'formik'
import * as Yup from 'yup'
import api from '../lib/axios'
import { toast } from 'react-toastify'

const schema = Yup.object({
  email: Yup.string().email('Email không hợp lệ').required('Bắt buộc'),
  code: Yup.string().trim().length(6, '6 ký tự').required('Bắt buộc'),
  newPassword: Yup.string().min(6, 'Tối thiểu 6 ký tự').required('Bắt buộc'),
  confirm: Yup.string().oneOf([Yup.ref('newPassword')], 'Mật khẩu không khớp').required('Bắt buộc')
})

export default function OtpReset(){
  const nav = useNavigate()
  const [sp] = useSearchParams()
  const qEmail = sp.get('email') || ''

  const form = useFormik({
    initialValues: { email: qEmail, code: '', newPassword: '', confirm: '' },
    validationSchema: schema,
    onSubmit: async (values, { setSubmitting }) => {
      try {
        const payload = { email: values.email.trim(), code: values.code.trim(), newPassword: values.newPassword }
        const res = await api.post('/api/otp/reset-password', payload)
        if (res?.data?.success) {
          toast.success('Đặt lại mật khẩu thành công. Vui lòng đăng nhập')
          nav('/login', { replace: true })
        } else throw new Error('Reset failed')
      } catch (err) {
        console.error(err)
        const msg = err?.response?.data?.error?.message || 'Reset failed'
        toast.error(msg)
      } finally {
        setSubmitting(false)
      }
    }
  })

  return (
    <Card className="mx-auto" style={{maxWidth: 520}}>
      <Card.Body>
        <Card.Title>Đặt lại mật khẩu bằng OTP</Card.Title>
        <form onSubmit={form.handleSubmit} noValidate>
          <div className="mb-3">
            <label className="form-label">Email</label>
            <input name="email" type="email" className={`form-control ${form.touched.email && form.errors.email ? 'is-invalid' : ''}`} value={form.values.email} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="you@example.com" required />
            {form.touched.email && form.errors.email ? (<div className="invalid-feedback">{form.errors.email}</div>) : null}
          </div>
          <div className="mb-3">
            <label className="form-label">Mã OTP</label>
            <input name="code" className={`form-control ${form.touched.code && form.errors.code ? 'is-invalid' : ''}`} value={form.values.code} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="123456" required />
            {form.touched.code && form.errors.code ? (<div className="invalid-feedback">{form.errors.code}</div>) : null}
          </div>
          <div className="mb-3">
            <label className="form-label">Mật khẩu mới</label>
            <input name="newPassword" type="password" className={`form-control ${form.touched.newPassword && form.errors.newPassword ? 'is-invalid' : ''}`} value={form.values.newPassword} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="••••••••" required />
            {form.touched.newPassword && form.errors.newPassword ? (<div className="invalid-feedback">{form.errors.newPassword}</div>) : null}
          </div>
          <div className="mb-3">
            <label className="form-label">Xác nhận mật khẩu</label>
            <input name="confirm" type="password" className={`form-control ${form.touched.confirm && form.errors.confirm ? 'is-invalid' : ''}`} value={form.values.confirm} onChange={form.handleChange} onBlur={form.handleBlur} placeholder="••••••••" required />
            {form.touched.confirm && form.errors.confirm ? (<div className="invalid-feedback">{form.errors.confirm}</div>) : null}
          </div>
          <Button type="submit" disabled={form.isSubmitting}>{form.isSubmitting ? '…' : 'Đặt lại mật khẩu'}</Button>
          <div className="small mt-3">Chưa có mã? <Link to={`/otp/request?mode=reset&email=${encodeURIComponent(form.values.email)}`}>Gửi mã</Link></div>
        </form>
      </Card.Body>
    </Card>
  )
}
```

