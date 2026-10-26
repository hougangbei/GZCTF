import { Role } from '@Api'

export const isBuiltinAdmin = (user?: { userName?: string | null; role?: Role | null } | null) =>
  user?.role === Role.Admin && user.userName?.toLowerCase() === 'admin'
