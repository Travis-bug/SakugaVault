import { Suspense, lazy } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { LoadingPanel } from "./components/LoadingPanel";
import { ProtectedRoute } from "./components/ProtectedRoute";

const CatalogPage = lazy(() =>
  import("./pages/CatalogPage").then((module) => ({ default: module.CatalogPage }))
);
const LoginPage = lazy(() =>
  import("./pages/LoginPage").then((module) => ({ default: module.LoginPage }))
);
const SearchPage = lazy(() =>
  import("./pages/SearchPage").then((module) => ({ default: module.SearchPage }))
);
const DownloadsPage = lazy(() =>
  import("./pages/DownloadsPage").then((module) => ({ default: module.DownloadsPage }))
);
const ProfilePage = lazy(() =>
  import("./pages/ProfilePage").then((module) => ({ default: module.ProfilePage }))
);
const WatchPage = lazy(() =>
  import("./pages/WatchPage").then((module) => ({ default: module.WatchPage }))
);

export default function App() {
  return (
    <Suspense
      fallback={
        <LoadingPanel
          title="Loading Client"
          message="Streaming the next route shell into the browser."
        />
      }
    >
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/" element={<CatalogPage />} />
          <Route
            path="/downloads"
            element={<DownloadsPage />}
          />
          <Route
            path="/search"
            element={<SearchPage />}
          />
          <Route
            path="/profile"
            element={<ProfilePage />}
          />
          <Route path="/watch/:animeId" element={<WatchPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Suspense>
  );
}
