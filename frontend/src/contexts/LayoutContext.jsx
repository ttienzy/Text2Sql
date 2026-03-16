import { createContext, useContext, useState } from 'react';

const LayoutContext = createContext();

export const useLayout = () => {
    const context = useContext(LayoutContext);
    if (!context) {
        throw new Error('useLayout must be used within a LayoutProvider');
    }
    return context;
};

export const LayoutProvider = ({ children }) => {
    const [sidebarVisible, setSidebarVisible] = useState(true);
    const [infoPanelVisible, setInfoPanelVisible] = useState(true);

    const toggleSidebar = () => setSidebarVisible(!sidebarVisible);
    const toggleInfoPanel = () => {
        // Chỉ cho phép ẩn InfoPanel trên các trang khác, không phải chat
        const isChatPage = window.location.pathname === '/chat';
        if (!isChatPage) {
            setInfoPanelVisible(!infoPanelVisible);
        }
    };

    return (
        <LayoutContext.Provider
            value={{
                sidebarVisible,
                infoPanelVisible,
                toggleSidebar,
                toggleInfoPanel,
            }}
        >
            {children}
        </LayoutContext.Provider>
    );
};