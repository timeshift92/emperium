
import * as React from 'react'
import { cn } from '../../utils/cn'
export const Textarea = React.forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className, ...props }, ref) => (<textarea ref={ref} className={cn('w-full min-h-[90px] rounded-md border border-amber-700/50 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-amber-600', className)} {...props} />)
); Textarea.displayName='Textarea'