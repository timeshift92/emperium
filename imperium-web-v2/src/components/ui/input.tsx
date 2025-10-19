
import * as React from 'react'
import { cn } from '../../utils/cn'
export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, ...props }, ref) => (<input ref={ref} className={cn('w-full rounded-md border border-amber-700/50 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-amber-600', className)} {...props} />)
); Input.displayName='Input'