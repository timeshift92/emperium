
import * as React from 'react'
import { cn } from '../../utils/cn'
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> { variant?: 'default'|'outline'|'ghost'; size?: 'sm'|'md' }
export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(({ className, variant='default', size='md', ...props }, ref) => {
  const base='inline-flex items-center justify-center rounded-md font-medium transition-colors'
  const variants={ default:'bg-amber-700 text-white hover:bg-amber-800', outline:'border border-amber-700 text-amber-800 hover:bg-amber-50', ghost:'hover:bg-amber-100' }
  const sizes={ sm:'px-3 py-1 text-xs', md:'px-4 py-2 text-sm' }
  return <button ref={ref} className={cn(base, variants[variant], sizes[size], className)} {...props} />
}); Button.displayName='Button'