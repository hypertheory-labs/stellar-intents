# Errands in a shop

**2026-10-26.** The third sample domain, written the same way as
[the library](errands-library.md) and [insurance](errands-insurance.md). Sketches, not
code, and every business answer is invented. Errands are resources (`PUT /errands/{id}`,
`DELETE /errands/{id}` to withdraw); reads stay where they are (`GET /orders/{id}`).

What this domain adds: money and shipping, which can't be taken back; a grace window on
purpose; errands addressed to an order, possible only while it's at certain points; and a
promise the shop fails to keep after it was accepted.

## Place an order

**Customer → Shop:** send me what's in my cart, at these prices, to this address, paid
this way.

| Ends      | When                                                                                                   |
| --------- | ------------------------------------------------------------------------------------------------------ |
| accepted  | The order is taken: paid, and committed to shipping.                                                   |
| declined  | The payment was refused; a price rose past tolerance; something is out of stock. Each with its reason. |
| withdrawn | Taken back inside the grace window.                                                                    |

- **The grace window, on purpose.** The shop doesn't decide for 30 minutes. The
  receipt says so: "We'll process your order at 10:42. Until then you can take it back."
  Withdrawing in the window is free, because nothing was charged and nothing was
  picked. This is the 3-second delay in the reference's holds, declared instead of
  accidental.
- **Offers, when declined** (closed):
  - payment refused → _pay another way_. That's a new errand, not a retry: the body
    changes, and the same id with a different body is a conflict.
  - a price rose → _order at the new prices_.
  - something is out of stock → _order without it_. Not "accept the rest": by the rule
    from insurance, getting less than they asked for is the person's choice.
- **After it's accepted, possible, while the order hasn't shipped:**
  [cancel the order](#cancel-an-order), [change the delivery address](#change-the-delivery-address).
- **Starts, when accepted:** _tell me when it ships_, by their preferred channels (a
  promise, so not the stream alone).
- **Projections changed:** the order, the customer's orders, the stock of each item, the
  cart.

**Where it strains: partial stock.** Real shops take the order and backorder or split.
The rule makes it a decline with _order without it_, so one missing item turns into a
round trip. I think the rule is still right, and the fix is upstream: the cart can warn
before checkout (the stock is a read, and the stream can say it changed), so the decline
is rare. If a business wants "ship what you have" by default, the customer says so ahead
of time, as a preference, and then getting less _is_ what they asked for.

## Cancel an order

**Customer → Shop:** I don't want this order any more.

After acceptance this is a forward errand, as in the library and insurance. Money moved,
so the shop compensates (a refund), but that's the shop's work, not the customer's.

| Ends     | When                                                                |
| -------- | ------------------------------------------------------------------- |
| accepted | Cancelled before it shipped. Refunded.                              |
| declined | It already shipped. Reason: "It's on its way; it shipped at 14:05." |

- **Offers, when declined:** _start a return_.
- **Possible only while:** the order hasn't shipped. Once it has, the shop doesn't offer
  cancel at all; it offers _start a return_. The decline above is for the race, when the
  cancel and the shipment cross.
- **Projections changed:** the order, the customer's orders, the stock.

## Change the delivery address

**Customer → Shop:** send order O to this address instead.

| Ends     | When                                  |
| -------- | ------------------------------------- |
| accepted | The order will go to the new address. |
| declined | It already shipped. Reason says so.   |

- **Offers, when declined:** none from the shop. (Redirecting a parcel is the carrier's
  errand, and closed world says we don't offer what we can't do.)
- **Possible only while:** the order hasn't shipped.
- **Projections changed:** the order.

**Your can of worms, opened a crack.** Cancel and change-the-address are errands
_addressed to an order_, possible only before it ships. It needed nothing more than a
table:

| The order is  | Errands it accepts                        |
| ------------- | ----------------------------------------- |
| in the window | (only withdrawing the place-order errand) |
| accepted      | cancel, change the address                |
| shipped       | start a return                            |
| delivered     | start a return (for 30 days)              |

That's "what else becomes possible", keyed by the thing's state. It's closed world, so
it can be reported and compared, and it isn't a dynamic state machine.

## When the shop can't keep its promise

After an order is accepted, the warehouse finds the last copy damaged.

This isn't an errand. Nobody asked for anything. And it isn't a new outcome, because
the place-order errand ended (accepted), and outcomes are final. It's the shop
**breaking a commitment** an accepted errand made. That's what an **apology** is:

> "We're sorry: the blue mug in your order was damaged in our warehouse. We've shipped
> the rest."

- **Offers** (closed): _refund it_, _send the green one instead_, _wait for a restock_.
  Each is an errand the customer can choose. If they choose nothing within some time,
  the shop refunds (a default, stated in the apology).
- **Delivered as a promise,** not a convenience: by the stream if they're online, and by
  their preferred channels as well. It goes in their history too, beside the errand
  whose commitment broke.

This gives "apology" its meaning back. Until now, apologies have been about the
transport (`try-again`, `sign-in`): our failures to deliver. This one is the business's
failure to keep a promise. So there are two kinds:

- **Before an outcome:** the transport failed. The library handles these, and they're
  the same in every app.
- **After an outcome:** a commitment broke. The app writes these, and their offers are
  errands.

## What the shop would report

| Errand                | Started by         | Withdraw window       | Offers (closed)                                        | Starts                | Possible after                             |
| --------------------- | ------------------ | --------------------- | ------------------------------------------------------ | --------------------- | ------------------------------------------ |
| place an order        | customer           | 30 minutes (declared) | pay another way; order at new prices; order without it | tell me when it ships | cancel; change the address (until shipped) |
| cancel an order       | customer           | none                  | start a return                                         | none                  | none                                       |
| change the address    | customer           | none                  | none                                                   | none                  | none                                       |
| tell me when it ships | shop, for customer | none                  | none                                                   | none                  | none                                       |
| start a return        | customer           | until decided         | none in this sketch                                    | none                  | none                                       |

| Apology (a broken commitment) | Of errand      | Offers (closed)                             | Default if unanswered |
| ----------------------------- | -------------- | ------------------------------------------- | --------------------- |
| an item can't be sent         | place an order | refund it; send another; wait for a restock | refund it             |

What the spec alone says: why 30 minutes, why "order without it" isn't automatic, why we
don't redirect parcels, and what the apology says.

## Found by writing it

- **The grace window works as a declared property.** Withdrawing inside it is free,
  because the decision hasn't happened. It's the holds' accidental delay, made honest,
  and the receipt can state it.
- **Errands addressed to a thing are a table** from the thing's state to the errands it
  accepts. It's closed and reportable. No state machine engine.
- **A broken commitment is an apology, not an outcome.** Outcomes stay final. The apology
  is a promised message with closed offers and a default. That splits apologies in two:
  the transport's (before an outcome, the library's) and the business's (after one, the
  app's).
- **Partial stock strains the "anything less is a decline" rule.** The rule holds if
  preferences can say "ship what you have", and the cart warns early. Worth watching.
- **A different payment is a new errand, not a retry.** The same id with a different body
  is already a conflict on the wire, which forces it.
