import { Outlet } from 'react-router-dom';

const AuthLayout = () => {
  return (
    <div style={{ width: '100%', height: '100vh' }}>
      <Outlet />
    </div>
  );
};

export default AuthLayout;
